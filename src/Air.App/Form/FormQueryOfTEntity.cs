namespace Platform.App.Form;

public sealed class FormQuery<TEntity>
  where TEntity : class
{
  internal IQueryable<TEntity>? Source { get; private set; }
  public List<Expression<Func<TEntity, bool>>> Filters { get; } = [];

  public FormQuery<TEntity> From(IQueryable<TEntity> source)
  {
    Source = source;
    return this;
  }

  public FormQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
  {
    Filters.Add(predicate);
    return this;
  }
}
