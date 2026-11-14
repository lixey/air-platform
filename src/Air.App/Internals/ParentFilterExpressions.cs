namespace Platform.App.Internals;

internal static class ParentFilterExpressions
{
  private static readonly MethodInfo ContainsLongMethod = typeof(Enumerable).GetMethods()
    .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
    .MakeGenericMethod(typeof(long));

  private static readonly MethodInfo ContainsNullableLongMethod = typeof(Enumerable).GetMethods()
    .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
    .MakeGenericMethod(typeof(long?));

  public static PropertyInfo? ExtractBoundProperty<TEntity, TProp>(Expression<Func<TEntity, TProp>> member)
  {
    var expr = member.Body is UnaryExpression ue ? ue.Operand : member.Body;
    return expr is MemberExpression { Member: PropertyInfo pi } ? pi : null;
  }

  public static Expression<Func<TEntity, bool>> BuildParentKeyFilter<TEntity>(PropertyInfo prop, object parentVal)
  {
    var param = Expression.Parameter(typeof(TEntity), "e");
    var propExpr = Expression.Property(param, prop);
    var constExpr = Expression.Constant(parentVal, prop.PropertyType);
    return Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(propExpr, constExpr), param);
  }

  public static Expression<Func<TEntity, bool>> BuildKeyInFilter<TEntity>(PropertyInfo prop, IReadOnlyCollection<long> ids)
  {
    var param = Expression.Parameter(typeof(TEntity), "e");
    Expression propExpr = Expression.Property(param, prop);
    if (prop.PropertyType == typeof(long?))
    {
      return BuildNullableKeyInLambda<TEntity>(param, propExpr, ids);
    }

    return BuildKeyInLambda<TEntity>(param, propExpr, ids);
  }

  public static Expression<Func<TEntity, bool>> BuildKeyInFilter<TEntity>(
    Expression<Func<TEntity, long>> keySelector,
    IReadOnlyCollection<long> ids) =>
    BuildKeyInLambda<TEntity>(keySelector.Parameters[0], keySelector.Body, ids);

  public static Expression<Func<TEntity, bool>> BuildKeyInFilter<TEntity>(
    Expression<Func<TEntity, long?>> keySelector,
    IReadOnlyCollection<long> ids) =>
    BuildNullableKeyInLambda<TEntity>(keySelector.Parameters[0], keySelector.Body, ids);

  private static Expression<Func<TEntity, bool>> BuildKeyInLambda<TEntity>(
    ParameterExpression param,
    Expression keyLongExpr,
    IReadOnlyCollection<long> ids)
  {
    var idList = ids as List<long> ?? [.. ids];
    var inListExpr = Expression.Call(ContainsLongMethod, Expression.Constant(idList), keyLongExpr);
    return Expression.Lambda<Func<TEntity, bool>>(inListExpr, param);
  }

  private static Expression<Func<TEntity, bool>> BuildNullableKeyInLambda<TEntity>(
    ParameterExpression param,
    Expression keyNullableLongExpr,
    IReadOnlyCollection<long> ids)
  {
    List<long?> nullableIds = [.. ids.Select(static id => (long?)id)];
    var inListExpr = Expression.Call(ContainsNullableLongMethod, Expression.Constant(nullableIds), keyNullableLongExpr);
    return Expression.Lambda<Func<TEntity, bool>>(inListExpr, param);
  }
}
