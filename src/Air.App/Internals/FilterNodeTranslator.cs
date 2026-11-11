namespace Platform.App.Internals;

internal static class FilterNodeTranslator
{
  public static FilterNode Translate(LambdaExpression predicate)
  {
    ArgumentNullException.ThrowIfNull(predicate);
    if (predicate.Parameters.Count != 1)
    {
      throw new NotSupportedException($"Filter predicate '{predicate}' must have exactly one parameter.");
    }

    return TranslateExpression(predicate.Body, predicate.Parameters[0]);
  }

  private static FilterNode TranslateExpression(Expression expr, ParameterExpression rootParam)
  {
    expr = UnwrapConvert(expr);

    if (expr is ConstantExpression { Value: true })
    {
      return FilterNode.All();
    }

    if (expr is BinaryExpression { NodeType: ExpressionType.AndAlso } andExpr)
    {
      return FilterNode.All(
        TranslateExpression(andExpr.Left, rootParam),
        TranslateExpression(andExpr.Right, rootParam));
    }

    if (expr is BinaryExpression { NodeType: ExpressionType.OrElse } orExpr)
    {
      return FilterNode.Any(
        TranslateExpression(orExpr.Left, rootParam),
        TranslateExpression(orExpr.Right, rootParam));
    }

    if (expr is UnaryExpression { NodeType: ExpressionType.Not } notExpr)
    {
      var inner = UnwrapConvert(notExpr.Operand);
      if (inner is UnaryExpression { NodeType: ExpressionType.Not } doubleNeg)
      {
        return TranslateExpression(doubleNeg.Operand, rootParam);
      }

      if (TryGetRootMemberPath(inner, rootParam, out var notMember, out _))
      {
        return FilterNode.Rule(notMember, FilterOperator.Equals, false);
      }

      if (inner is MethodCallExpression notCall && TryTranslateMethodCall(notCall, rootParam, negated: true, out var negatedCallNode))
      {
        return negatedCallNode!;
      }

      if (inner is BinaryExpression innerBin)
      {
        return TranslateInvertedBinary(innerBin, rootParam);
      }

      throw new NotSupportedException($"Filter predicate negation '{notExpr}' is not supported by FilterNode.");
    }

    if (expr is MethodCallExpression call && TryTranslateMethodCall(call, rootParam, negated: false, out var callNode))
    {
      return callNode!;
    }

    if (expr is BinaryExpression bin)
    {
      return TranslateBinary(bin, rootParam, invert: false);
    }

    if (TryGetRootMemberPath(expr, rootParam, out var boolMember, out _))
    {
      return FilterNode.Rule(boolMember, FilterOperator.Equals, true);
    }

    throw new NotSupportedException($"Filter predicate expression '{expr}' cannot be translated to a declarative FilterNode.");
  }

  private static FilterNode TranslateInvertedBinary(BinaryExpression bin, ParameterExpression rootParam) =>
    bin.NodeType switch
    {
      ExpressionType.AndAlso => FilterNode.Any(
        TranslateExpression(Expression.Not(bin.Left), rootParam),
        TranslateExpression(Expression.Not(bin.Right), rootParam)),
      ExpressionType.OrElse => FilterNode.All(
        TranslateExpression(Expression.Not(bin.Left), rootParam),
        TranslateExpression(Expression.Not(bin.Right), rootParam)),
      _ => TranslateBinary(bin, rootParam, invert: true)
    };

  private static FilterNode TranslateBinary(BinaryExpression bin, ParameterExpression rootParam, bool invert)
  {
    var leftIsField = TryGetRootMemberPath(bin.Left, rootParam, out var fieldPath, out var isNullableField);
    var valExpr = bin.Right;
    var swapped = false;

    if (!leftIsField)
    {
      if (!TryGetRootMemberPath(bin.Right, rootParam, out fieldPath, out isNullableField))
      {
        throw new NotSupportedException($"Binary filter expression '{bin}' must reference a member of '{rootParam.Name}'.");
      }

      valExpr = bin.Left;
      swapped = true;
    }

    if (ReferencesParameter(valExpr, rootParam))
    {
      throw new NotSupportedException($"Field-to-field filter comparison '{bin}' is not supported by FilterNode.");
    }

    var val = NormalizeRuleValue(EvaluateConstantOrClosure(valExpr));

    var nodeType = bin.NodeType;
    if (swapped)
    {
      nodeType = nodeType switch
      {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => nodeType
      };
    }

    if (invert)
    {
      nodeType = nodeType switch
      {
        ExpressionType.Equal => ExpressionType.NotEqual,
        ExpressionType.NotEqual => ExpressionType.Equal,
        ExpressionType.GreaterThan => ExpressionType.LessThanOrEqual,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThan,
        ExpressionType.LessThan => ExpressionType.GreaterThanOrEqual,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThan,
        _ => throw new NotSupportedException($"Binary operator '{bin.NodeType}' cannot be negated in FilterNode.")
      };
    }

    if (val is null)
    {
      return nodeType switch
      {
        ExpressionType.Equal => FilterNode.Rule(fieldPath, FilterOperator.IsNull),
        ExpressionType.NotEqual => FilterNode.Rule(fieldPath, FilterOperator.IsNotNull),
        _ => throw new NotSupportedException($"Null comparison '{bin}' only supports '==' and '!='.")
      };
    }

    var op = nodeType switch
    {
      ExpressionType.Equal => FilterOperator.Equals,
      ExpressionType.NotEqual => FilterOperator.NotEquals,
      ExpressionType.GreaterThan => FilterOperator.GreaterThan,
      ExpressionType.GreaterThanOrEqual => FilterOperator.GreaterThanOrEqual,
      ExpressionType.LessThan => FilterOperator.LessThan,
      ExpressionType.LessThanOrEqual => FilterOperator.LessThanOrEqual,
      _ => throw new NotSupportedException($"Binary operator '{bin.NodeType}' in '{bin}' is not supported by FilterNode.")
    };

    var rule = FilterNode.Rule(fieldPath, op, val);
    if (invert &&
        isNullableField &&
        op is FilterOperator.LessThan or FilterOperator.LessThanOrEqual or FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual)
    {
      return FilterNode.Any(FilterNode.Rule(fieldPath, FilterOperator.IsNull), rule);
    }

    return rule;
  }

  private static bool TryTranslateMethodCall(
    MethodCallExpression call,
    ParameterExpression rootParam,
    bool negated,
    out FilterNode? node)
  {
    node = null;

    if (call.Method.DeclaringType == typeof(string) &&
        call.Method.Name is nameof(string.IsNullOrEmpty) or nameof(string.IsNullOrWhiteSpace) &&
        call.Arguments.Count == 1 &&
        TryGetRootMemberPath(call.Arguments[0], rootParam, out var nullField, out _))
    {
      node = FilterNode.Rule(nullField, negated ? FilterOperator.IsNotNull : FilterOperator.IsNull);
      return true;
    }

    if (call.Method.DeclaringType == typeof(string) &&
        call.Object != null &&
        TryGetRootMemberPath(call.Object, rootParam, out var strField, out _) &&
        call.Arguments.Count >= 1 &&
        !ReferencesParameter(call.Arguments[0], rootParam))
    {
      var argVal = NormalizeRuleValue(EvaluateConstantOrClosure(call.Arguments[0]));
      if (call.Method.Name == nameof(string.Contains))
      {
        node = FilterNode.Rule(strField, negated ? FilterOperator.NotContains : FilterOperator.Contains, argVal);
        return true;
      }

      if (!negated && call.Method.Name == nameof(string.StartsWith))
      {
        node = FilterNode.Rule(strField, FilterOperator.StartsWith, argVal);
        return true;
      }

      if (!negated && call.Method.Name == nameof(string.EndsWith))
      {
        node = FilterNode.Rule(strField, FilterOperator.EndsWith, argVal);
        return true;
      }
    }

    if (call.Method.Name == nameof(Enumerable.Contains))
    {
      Expression? collectionExpr = null;
      Expression? itemExpr = null;
      if (call.Object != null && call.Arguments.Count == 1)
      {
        collectionExpr = call.Object;
        itemExpr = call.Arguments[0];
      }
      else if (call.Object == null && call.Arguments.Count == 2)
      {
        collectionExpr = call.Arguments[0];
        itemExpr = call.Arguments[1];
      }

      if (collectionExpr != null &&
          itemExpr != null &&
          TryGetRootMemberPath(itemExpr, rootParam, out var inField, out _) &&
          !ReferencesParameter(collectionExpr, rootParam))
      {
        var rawSeq = EvaluateConstantOrClosure(collectionExpr);
        var items = rawSeq is IEnumerable seq and not string
          ? seq.Cast<object?>().Select(NormalizeRuleValue).ToList()
          : [];
        node = FilterNode.Rule(inField, negated ? FilterOperator.NotIn : FilterOperator.In, items);
        return true;
      }
    }

    return false;
  }

  private static bool TryGetRootMemberPath(
    Expression expr,
    ParameterExpression rootParam,
    out string camelPath,
    out bool isNullableField)
  {
    camelPath = string.Empty;
    isNullableField = false;

    var cur = UnwrapConvert(expr);
    var explicitValueUnwrap = false;
    if (cur is MemberExpression { Member.Name: "Value", Expression: { } nullableInner } &&
        Nullable.GetUnderlyingType(nullableInner.Type) != null)
    {
      explicitValueUnwrap = true;
      cur = UnwrapConvert(nullableInner);
    }

    if (cur is not MemberExpression)
    {
      return false;
    }

    var segments = new List<string>(2);
    while (cur is MemberExpression me)
    {
      var isLeaf = segments.Count == 0;
      if ((!isLeaf || !explicitValueUnwrap) &&
          (!me.Type.IsValueType || Nullable.GetUnderlyingType(me.Type) != null))
      {
        isNullableField = true;
      }

      segments.Add(PropertyAccessorCache.ToCamel(me.Member.Name));
      cur = me.Expression != null ? UnwrapConvert(me.Expression) : null;
    }

    if (cur != rootParam || segments.Count == 0)
    {
      isNullableField = false;
      return false;
    }

    segments.Reverse();
    camelPath = segments.Count == 1 ? segments[0] : string.Join('.', segments);
    return true;
  }

  private static Expression UnwrapConvert(Expression expr)
  {
    while (expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conv)
    {
      expr = conv.Operand;
    }

    return expr;
  }

  private static bool ReferencesParameter(Expression? expr, ParameterExpression target)
  {
    if (expr is null)
    {
      return false;
    }

    return expr switch
    {
      ParameterExpression pe => pe == target,
      ConstantExpression => false,
      MemberExpression me => ReferencesParameter(me.Expression, target),
      UnaryExpression ue => ReferencesParameter(ue.Operand, target),
      BinaryExpression be => ReferencesParameter(be.Left, target) || ReferencesParameter(be.Right, target),
      ConditionalExpression ce => ReferencesParameter(ce.Test, target) || ReferencesParameter(ce.IfTrue, target) || ReferencesParameter(ce.IfFalse, target),
      MethodCallExpression mce => ReferencesParameter(mce.Object, target) || mce.Arguments.Any(a => ReferencesParameter(a, target)),
      NewArrayExpression nae => nae.Expressions.Any(e => ReferencesParameter(e, target)),
      NewExpression ne => ne.Arguments.Any(a => ReferencesParameter(a, target)),
      InvocationExpression ie => ReferencesParameter(ie.Expression, target) || ie.Arguments.Any(a => ReferencesParameter(a, target)),
      _ => true
    };
  }

  private static object? EvaluateConstantOrClosure(Expression expr)
  {
    var unwrapped = UnwrapConvert(expr);
    switch (unwrapped)
    {
      case ConstantExpression ce:
        return ConvertIfNeeded(ce.Value, expr.Type);

      case MemberExpression me:
        {
          var instance = me.Expression != null ? EvaluateConstantOrClosure(me.Expression) : null;
          var raw = me.Member switch
          {
            FieldInfo fi => fi.GetValue(instance),
            PropertyInfo pi => pi.GetValue(instance),
            _ => Expression.Lambda(expr).Compile(preferInterpretation: true).DynamicInvoke()
          };
          return ConvertIfNeeded(raw, expr.Type);
        }

      case NewArrayExpression { NodeType: ExpressionType.NewArrayInit } nae:
        return nae.Expressions.Select(EvaluateConstantOrClosure).ToList();

      default:
        return Expression.Lambda(expr).Compile(preferInterpretation: true).DynamicInvoke();
    }
  }

  private static object? ConvertIfNeeded(object? value, Type targetType)
  {
    if (value is null)
    {
      return null;
    }

    var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
    if (nonNullable.IsEnum)
    {
      return Convert.ChangeType(value, Enum.GetUnderlyingType(nonNullable), CultureInfo.InvariantCulture);
    }

    return value;
  }

  private static object? NormalizeRuleValue(object? val)
  {
    if (val is null)
    {
      return null;
    }

    var t = val.GetType();
    if (t.IsEnum)
    {
      return Convert.ChangeType(val, Enum.GetUnderlyingType(t), CultureInfo.InvariantCulture);
    }

    return val switch
    {
      DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      _ => val
    };
  }
}
