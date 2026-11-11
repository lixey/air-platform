namespace Platform.App.Grid;

public class Columns<TRow> : Columns
{
  public GridColumn<TRow, TRow, TProp> Add<TProp>(Expression<Func<TRow, TProp?>> expression) =>
    base.Add(expression);

  public GridColumn<TRow, TRow, TProp> Add<TProp>(string path, Expression<Func<TRow, TProp>> expression) =>
    base.Add(path, expression);

  public new GridColumn<TRow, TRow, TProp> Add<TProp>(string path) =>
    new(base.Add<TProp>(path).Dto, this);
}
