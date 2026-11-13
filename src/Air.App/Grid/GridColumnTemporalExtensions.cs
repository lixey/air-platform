namespace Platform.App.Grid;

public static class GridColumnTemporalExtensions
{
  public static GridColumn<TEntity, TRow, DateOnly> AsDate<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateOnly> column) =>
    ApplyDate(column);

  public static GridColumn<TEntity, TRow, DateOnly?> AsDate<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateOnly?> column) =>
    ApplyDate(column);

  public static GridColumn<TEntity, TRow, DateTime> AsDate<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTime> column) =>
    ApplyDate(column);

  public static GridColumn<TEntity, TRow, DateTime?> AsDate<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTime?> column) =>
    ApplyDate(column);

  public static GridColumn<TEntity, TRow, DateTimeOffset> AsDate<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTimeOffset> column) =>
    ApplyDate(column);

  public static GridColumn<TEntity, TRow, DateTimeOffset?> AsDate<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTimeOffset?> column) =>
    ApplyDate(column);

  public static GridColumn<TEntity, TRow, DateTime> AsDateTime<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTime> column) =>
    ApplyDateTime(column);

  public static GridColumn<TEntity, TRow, DateTime?> AsDateTime<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTime?> column) =>
    ApplyDateTime(column);

  public static GridColumn<TEntity, TRow, DateTimeOffset> AsDateTime<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTimeOffset> column) =>
    ApplyDateTime(column);

  public static GridColumn<TEntity, TRow, DateTimeOffset?> AsDateTime<TEntity, TRow>(
    this GridColumn<TEntity, TRow, DateTimeOffset?> column) =>
    ApplyDateTime(column);

  private static GridColumn<TEntity, TRow, T> ApplyDate<TEntity, TRow, T>(GridColumn<TEntity, TRow, T> column)
  {
    column.Dto.Type = UiFieldType.Date;
    column.Dto.Cell = new GridCellDateModel();
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyDateTime<TEntity, TRow, T>(GridColumn<TEntity, TRow, T> column)
  {
    column.Dto.Type = UiFieldType.DateTime;
    column.Dto.Cell = new GridCellDateTimeModel();
    return column;
  }
}
