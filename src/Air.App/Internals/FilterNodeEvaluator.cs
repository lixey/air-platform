namespace Platform.App.Internals;

internal static class FilterNodeEvaluator
{
  public static FilterNode Translate(
    Expression expr,
    Func<Expression, string?> getMemberName,
    Func<string, string> toCamel)
  {
    if (expr is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conv)
    {
      expr = conv.Operand;
    }

    if (expr is BinaryExpression { NodeType: ExpressionType.AndAlso } andExpr)
    {
      return FilterNode.All(Translate(andExpr.Left, getMemberName, toCamel), Translate(andExpr.Right, getMemberName, toCamel));
    }

    if (expr is BinaryExpression { NodeType: ExpressionType.OrElse } orExpr)
    {
      return FilterNode.Any(Translate(orExpr.Left, getMemberName, toCamel), Translate(orExpr.Right, getMemberName, toCamel));
    }

    if (expr is UnaryExpression { NodeType: ExpressionType.Not } notExpr && getMemberName(notExpr.Operand) is { } notMember)
    {
      return FilterNode.Rule(toCamel(notMember), FilterOperator.Equals, false);
    }

    if (expr is BinaryExpression bin)
    {
      var fieldName = getMemberName(bin.Left);
      var valExpr = bin.Right;
      if (fieldName == null)
      {
        fieldName = getMemberName(bin.Right);
        valExpr = bin.Left;
      }

      if (fieldName != null)
      {
        var camelField = toCamel(fieldName);
        object? val = null;
        try
        {
          val = valExpr is ConstantExpression ce
            ? ce.Value
            : Expression.Lambda(valExpr).Compile().DynamicInvoke();
        }
        catch { }

        if (val == null)
        {
          return bin.NodeType == ExpressionType.NotEqual
            ? FilterNode.Rule(camelField, FilterOperator.IsNotNull)
            : FilterNode.Rule(camelField, FilterOperator.IsNull);
        }

        var op = bin.NodeType switch
        {
          ExpressionType.NotEqual => FilterOperator.NotEquals,
          ExpressionType.GreaterThan => FilterOperator.GreaterThan,
          ExpressionType.GreaterThanOrEqual => FilterOperator.GreaterThanOrEqual,
          ExpressionType.LessThan => FilterOperator.LessThan,
          ExpressionType.LessThanOrEqual => FilterOperator.LessThanOrEqual,
          _ => FilterOperator.Equals
        };
        return FilterNode.Rule(camelField, op, val);
      }
    }

    if (getMemberName(expr) is { } boolMember)
    {
      return FilterNode.Rule(toCamel(boolMember), FilterOperator.Equals, true);
    }

    return FilterNode.All();
  }

  public static bool Evaluate(FilterNode node, object? primary, object? fallback) =>
    Evaluate(node, field =>
      primary != null && PropertyAccessorCache.GetProperty(primary.GetType(), field) != null
        ? PropertyAccessorCache.GetValue(primary, field)
        : fallback != null
          ? PropertyAccessorCache.GetValue(fallback, field)
          : null);

  public static bool Evaluate(FilterNode node, Func<string, object?> valueResolver)
  {
    if (node is FilterGroup group)
    {
      if (group.Items.Count == 0)
      {
        return true;
      }

      return FilterLogic.IsOr(group.Logic)
        ? group.Items.Any(i => Evaluate(i, valueResolver))
        : group.Items.All(i => Evaluate(i, valueResolver));
    }

    if (node is not FilterRule rule || string.IsNullOrEmpty(rule.Field))
    {
      return false;
    }

    var val = valueResolver(rule.Field);
    var op = rule.Operator;
    if (string.Equals(op, FilterOperator.IsNull, StringComparison.OrdinalIgnoreCase))
    {
      return val is null || val is string s && string.IsNullOrEmpty(s);
    }

    if (string.Equals(op, FilterOperator.IsNotNull, StringComparison.OrdinalIgnoreCase))
    {
      return val is not null && (val is not string s || !string.IsNullOrEmpty(s));
    }

    if (string.Equals(op, FilterOperator.In, StringComparison.OrdinalIgnoreCase))
    {
      return rule.Value is IEnumerable seq && seq is not string && seq.Cast<object?>().Any(x => CompareValues(val, x) == 0);
    }

    if (string.Equals(op, FilterOperator.NotIn, StringComparison.OrdinalIgnoreCase))
    {
      return rule.Value is not IEnumerable seq || seq is string || !seq.Cast<object?>().Any(x => CompareValues(val, x) == 0);
    }

    var cmp = CompareValues(val, rule.Value);
    return op switch
    {
      FilterOperator.Equals => cmp == 0,
      FilterOperator.NotEquals => cmp != 0,
      FilterOperator.GreaterThan => cmp is > 0,
      FilterOperator.GreaterThanOrEqual => cmp is >= 0,
      FilterOperator.LessThan => cmp is < 0,
      FilterOperator.LessThanOrEqual => cmp is <= 0,
      _ => false
    };
  }

  public static Dictionary<string, object?> ExtractEqualityValues(FilterNode? node)
  {
    var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    ExtractEqualityValues(node, map);
    return map;
  }

  public static void ExtractEqualityValues(FilterNode? node, IDictionary<string, object?> target)
  {
    if (node is null)
    {
      return;
    }

    if (node is FilterRule rule)
    {
      if (string.Equals(rule.Operator, FilterOperator.Equals, StringComparison.OrdinalIgnoreCase) &&
          !string.IsNullOrWhiteSpace(rule.Field) &&
          rule.Value is not null)
      {
        target[rule.Field] = UnwrapJsonValue(rule.Value);
      }

      return;
    }

    if (node is FilterGroup group && !FilterLogic.IsOr(group.Logic))
    {
      foreach (var item in group.Items)
      {
        ExtractEqualityValues(item, target);
      }
    }
  }

  public static int? CompareValues(object? left, object? right)
  {
    left = UnwrapJsonValue(left);
    right = UnwrapJsonValue(right);

    if (left is null && right is null)
    {
      return 0;
    }

    if (left is null || right is null)
    {
      return null;
    }

    if (TryConvertToDecimal(left, out var leftDec) && TryConvertToDecimal(right, out var rightDec))
    {
      return leftDec.CompareTo(rightDec);
    }

    if (left is bool lb && right is bool rb)
    {
      return lb.CompareTo(rb);
    }

    return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
  }

  public static bool TryConvertToDecimal(object? value, out decimal result)
  {
    switch (value)
    {
      case null:
        result = 0m;
        return false;
      case decimal d: result = d; return true;
      case int i: result = i; return true;
      case long l: result = l; return true;
      case short s: result = s; return true;
      case byte b: result = b; return true;
      case double dbVal and not double.NaN and not double.PositiveInfinity and not double.NegativeInfinity:
        result = (decimal)dbVal; return true;
      case float flVal and not float.NaN and not float.PositiveInfinity and not float.NegativeInfinity:
        result = (decimal)flVal; return true;
      default:
        result = 0; return false;
    }
  }

  private static object? UnwrapJsonValue(object? val) => val is JsonElement je
    ? je.ValueKind switch
    {
      JsonValueKind.Null or JsonValueKind.Undefined => null,
      JsonValueKind.True => true,
      JsonValueKind.False => false,
      JsonValueKind.Number when je.TryGetInt64(out var l) => l,
      JsonValueKind.Number when je.TryGetDecimal(out var d) => d,
      JsonValueKind.String => je.GetString(),
      _ => je.ToString()
    }
    : val;
}
