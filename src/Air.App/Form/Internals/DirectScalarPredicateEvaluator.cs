using System.Runtime.CompilerServices;

namespace Platform.App.Form.Internals;

internal static class DirectScalarPredicateEvaluator
{
  private static readonly ConditionalWeakTable<LambdaExpression, object?> CompiledCache = [];

  public static bool TryEvaluateAll<TEntity>(
    TEntity entity,
    IReadOnlyList<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)> offers,
    out Dictionary<string, int>? unavailable)
    where TEntity : class, IEntity
  {
    unavailable = null;
    var compiled = new Func<TEntity, bool>[offers.Count];
    for (var i = 0; i < offers.Count; i++)
    {
      var fn = GetOrCompileSafe(offers[i].Condition);
      if (fn == null)
      {
        return false;
      }

      compiled[i] = fn;
    }

    for (var i = 0; i < offers.Count; i++)
    {
      if (!compiled[i](entity))
      {
        unavailable ??= new Dictionary<string, int>(StringComparer.Ordinal);
        unavailable.TryAdd(offers[i].ActionKey, offers[i].ReasonIndex);
      }
    }

    return true;
  }

  private static Func<TEntity, bool>? GetOrCompileSafe<TEntity>(Expression<Func<TEntity, bool>> condition)
    where TEntity : class
  {
    var cached = CompiledCache.GetValue(
      condition,
      static expr => IsSafeNode(expr.Body, expr.Parameters[0]) ? expr.Compile(preferInterpretation: true) : null);
    return cached as Func<TEntity, bool>;
  }

  private static bool IsSafeNode(Expression? node, ParameterExpression param) =>
    node switch
    {
      null => true,
      ConstantExpression c => c.Value == null || IsDirectValueScalarType(c.Type),
      UnaryExpression u when u.NodeType is ExpressionType.Not or ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Negate =>
        IsSafeNode(u.Operand, param),
      BinaryExpression b when b.NodeType is
        ExpressionType.Equal or ExpressionType.NotEqual or
        ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or
        ExpressionType.LessThan or ExpressionType.LessThanOrEqual or
        ExpressionType.AndAlso or ExpressionType.OrElse or
        ExpressionType.Add or ExpressionType.Subtract or ExpressionType.Multiply or
        ExpressionType.Coalesce =>
        IsSafeNode(b.Left, param) && IsSafeNode(b.Right, param),
      MemberExpression { Expression: ParameterExpression p, Member: PropertyInfo prop } when p == param =>
        IsDirectValueScalarType(prop.PropertyType),
      MemberExpression { Expression: MemberExpression { Expression: ParameterExpression p, Member: PropertyInfo outerProp }, Member.Name: "HasValue" or "Value" }
        when p == param && Nullable.GetUnderlyingType(outerProp.PropertyType) is { } underlying =>
        IsDirectValueScalarType(underlying),
      MemberExpression { Expression: ConstantExpression closure } me when closure.Value != null =>
        GetClosureMemberValueType(me.Member) is { } closureMemberType && IsDirectValueScalarType(closureMemberType),
      MemberExpression { Expression: null } me =>
        GetClosureMemberValueType(me.Member) is { } staticMemberType && IsDirectValueScalarType(staticMemberType),
      _ => false
    };

  private static Type? GetClosureMemberValueType(MemberInfo member) =>
    member switch
    {
      FieldInfo fi => fi.FieldType,
      PropertyInfo pi => pi.PropertyType,
      _ => null
    };

  private static bool IsDirectValueScalarType(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    return underlying.IsPrimitive ||
           underlying.IsEnum ||
           underlying == typeof(decimal) ||
           underlying == typeof(DateTime) ||
           underlying == typeof(DateTimeOffset) ||
           underlying == typeof(DateOnly) ||
           underlying == typeof(TimeOnly) ||
           underlying == typeof(Guid);
  }
}
