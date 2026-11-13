namespace Platform.App.Grid;

public static class GridColumnTextExtensions
{
  public static GridColumn<TEntity, TRow, TText> AsPhone<TEntity, TRow, TText>(
    this GridColumn<TEntity, TRow, TText> column)
    where TText : IEquatable<string>?
  {
    column.Dto.Type = UiFieldType.Text;
    column.Dto.Cell = new GridCellPhoneModel();
    return column;
  }

  public static GridColumn<TEntity, TRow, TText> AsEmail<TEntity, TRow, TText>(
    this GridColumn<TEntity, TRow, TText> column)
    where TText : IEquatable<string>?
  {
    column.Dto.Type = UiFieldType.Text;
    column.Dto.Cell = new GridCellEmailModel();
    return column;
  }

  public static GridColumn<TEntity, TRow, TText> AsUrl<TEntity, TRow, TText>(
    this GridColumn<TEntity, TRow, TText> column)
    where TText : IEquatable<string>?
  {
    column.Dto.Type = UiFieldType.Text;
    column.Dto.Cell = new GridCellUrlModel();
    return column;
  }
}
