namespace Platform.App.Grid;

public sealed class GridQueryInitialFilterBuilder<TEntity, TRow, TProp>(
  GridQuery<TEntity, TRow> query,
  string field)
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public GridQuery<TEntity, TRow> In(params TProp[] values)
  {
    if (!string.IsNullOrEmpty(field) && values.Length > 0)
    {
      query.InitialFilters.Add(FilterNode.Rule(field, FilterOperator.In, values.Cast<object?>().ToList()));
    }

    return query;
  }
}
