namespace Platform.App.Grid;

public class GridColumn<TEntity, TRow, TProp>(GridColumnDto dto, Columns owner) : GridColumn<TEntity, TProp>(dto, owner)
{
  public new GridColumn<TEntity, TRow, TProp> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new GridColumn<TEntity, TRow, TProp> Title(string uiSource, string dbSource)
  {
    base.Title(uiSource, dbSource);
    return this;
  }

  public new GridColumn<TEntity, TRow, TProp> Format(Func<TProp, object?> formatter)
  {
    base.Format(formatter);
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> OpenForm(Func<TRow, Type?> resolver)
  {
    Dto.Cell = new GridCellOpenFormModel();
    Dto.Width ??= 80;
    Dto.IsSticky = true;
    Dto.FormServiceResolver = row => row is TRow typed ? resolver(typed) : null;
    return this;
  }
}
