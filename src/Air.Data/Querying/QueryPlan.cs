namespace Platform.Data.Querying;

/// <summary>
/// A validated, compiled query plan ready for execution: filter predicate, deterministic ordering, pagination.
/// </summary>
public sealed class QueryPlan<TEntity> where TEntity : class
{
  internal QueryPlan(Expression<Func<TEntity, bool>>? predicate, IReadOnlyList<SortKey> ordering, int page, int pageSize)
  {
    Predicate = predicate;
    Ordering = ordering;
    Page = page;
    PageSize = pageSize;
  }

  public Expression<Func<TEntity, bool>>? Predicate { get; }
  public IReadOnlyList<SortKey> Ordering { get; }
  public int Page { get; }
  public int PageSize { get; }

  public IQueryable<TEntity> Filter(IQueryable<TEntity> source) =>
    Predicate is null ? source : source.Where(Predicate);

  public IQueryable<TEntity> Order(IQueryable<TEntity> source)
  {
    var expression = source.Expression;
    for (var i = 0; i < Ordering.Count; i++)
    {
      var key = Ordering[i];
      var method = (i == 0, key.Descending) switch
      {
        (true, false) => QueryableMethods.OrderBy,
        (true, true) => QueryableMethods.OrderByDescending,
        (false, false) => QueryableMethods.ThenBy,
        (false, true) => QueryableMethods.ThenByDescending
      };
      expression = Expression.Call(
        method.MakeGenericMethod(typeof(TEntity), key.Selector.ReturnType),
        expression,
        Expression.Quote(key.Selector));
    }

    return source.Provider.CreateQuery<TEntity>(expression);
  }
}
