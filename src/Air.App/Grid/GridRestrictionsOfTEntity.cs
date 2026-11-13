namespace Platform.App.Grid;

public sealed class GridRestrictions<TEntity>
  where TEntity : class
{
  public List<Expression<Func<TEntity, bool>>> Filters { get; } = [];

  public GridRestrictions<TEntity> Restrict(Expression<Func<TEntity, bool>> predicate)
  {
    ArgumentNullException.ThrowIfNull(predicate);
    Filters.Add(predicate);
    return this;
  }

  public void Clear() => Filters.Clear();
}
