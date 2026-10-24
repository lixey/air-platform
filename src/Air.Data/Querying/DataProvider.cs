namespace Platform.Data.Querying;

public static class DataProvider
{
  public static DataProvider<TEntity, TRow> Create<TEntity, TRow>(
    IQueryable<TEntity> source,
    QueryMap<TEntity> map,
    Expression<Func<TEntity, TRow>> selector)
    where TEntity : class
    where TRow : class => new(source, map, selector);

  public static DataProvider<TEntity, TEntity> Create<TEntity>(
    IQueryable<TEntity> source,
    QueryMap<TEntity> map)
    where TEntity : class => new(source, map, entity => entity);
}
