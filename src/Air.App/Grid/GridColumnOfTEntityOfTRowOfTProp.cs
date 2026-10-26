namespace Platform.App.Grid;

public class GridColumn<TEntity, TRow, TProp>(GridColumnDto dto, Columns owner) : GridColumn<TEntity, TProp>(dto, owner)
{
  public GridColumn<TEntity, TRow, TProp> OpenForm(Func<TRow, Type?> resolver)
  {
    Dto.Cell = new GridCellOpenFormModel();
    Dto.Width ??= 80;
    Dto.IsSticky = true;
    Dto.FormServiceResolver = row => row is TRow typed ? resolver(typed) : null;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> EditableWhen(Expression<Func<TRow, bool>> predicate)
  {
    ApplyEditableWhen(predicate);
    return this;
  }
}
