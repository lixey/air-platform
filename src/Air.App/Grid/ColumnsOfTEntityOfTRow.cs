namespace Platform.App.Grid;

public class Columns<TEntity, TRow> : Columns<TRow>
{
  public new GridColumn<TEntity, TRow, TProp> Add<TProp>(Expression<Func<TRow, TProp?>> expression) =>
    new(base.Add(expression).Dto, this);

  public new GridColumn<TEntity, TRow, TProp> Add<TProp>(string path, Expression<Func<TRow, TProp>> expression) =>
    new(base.Add(path, expression).Dto, this);

  public new GridColumn<TEntity, TRow, TProp> Add<TProp>(string path) =>
    new(base.Add<TProp>(path).Dto, this);
}
