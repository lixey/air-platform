namespace Platform.Data.Querying;

public static class QueryMap
{
  public static QueryMapBuilder<TEntity> For<TEntity>() where TEntity : class => new();

  public static QueryMapBuilder<TEntity> FromSelector<TEntity, TRow>(Expression<Func<TEntity, TRow>> selector)
    where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(selector);
    return new QueryMapBuilder<TEntity>().AddSelector(selector);
  }

  public static QueryMapBuilder<TEntity> FromEntity<TEntity>() where TEntity : class =>
    new QueryMapBuilder<TEntity>().AddEntityProperties();
}
