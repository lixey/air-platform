namespace Platform.App.Grid;

public static class GridColumnBoolExtensions
{
  public static GridColumn<TEntity, TRow, bool> AsCheckbox<TEntity, TRow>(
    this GridColumn<TEntity, TRow, bool> column) =>
    ApplyCheckbox(column);

  public static GridColumn<TEntity, TRow, bool?> AsCheckbox<TEntity, TRow>(
    this GridColumn<TEntity, TRow, bool?> column) =>
    ApplyCheckbox(column);

  private static GridColumn<TEntity, TRow, T> ApplyCheckbox<TEntity, TRow, T>(GridColumn<TEntity, TRow, T> column)
  {
    column.Dto.Type = UiFieldType.Checkbox;
    column.Dto.Cell = new GridCellCheckboxModel();
    return column;
  }
}
