using Platform.App.Grid.Internals;

namespace Platform.App.Grid;

public static class GridColumnNumberExtensions
{
  public static GridColumn<TEntity, TRow, TProp> AsNumber<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column)
    where TProp : struct, INumber<TProp> =>
    ApplyNumber(column);

  public static GridColumn<TEntity, TRow, TProp?> AsNumber<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column)
    where TProp : struct, INumber<TProp> =>
    ApplyNumber(column);

  public static GridColumn<TEntity, TRow, TProp> Decimals<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    int decimals)
    where TProp : struct, INumber<TProp> =>
    ApplyDecimals(column, decimals);

  public static GridColumn<TEntity, TRow, TProp?> Decimals<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    int decimals)
    where TProp : struct, INumber<TProp> =>
    ApplyDecimals(column, decimals);

  public static GridColumn<TEntity, TRow, TProp> Grouped<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    bool isGrouped = true)
    where TProp : struct, INumber<TProp> =>
    ApplyGrouped(column, isGrouped);

  public static GridColumn<TEntity, TRow, TProp?> Grouped<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    bool isGrouped = true)
    where TProp : struct, INumber<TProp> =>
    ApplyGrouped(column, isGrouped);

  public static GridColumn<TEntity, TRow, TProp> AsMoney<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    string? currency = null)
    where TProp : struct, INumber<TProp> =>
    ApplyMoney(column, currency);

  public static GridColumn<TEntity, TRow, TProp?> AsMoney<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    string? currency = null)
    where TProp : struct, INumber<TProp> =>
    ApplyMoney(column, currency);

  public static GridColumn<TEntity, TRow, TProp> AsPercent<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column)
    where TProp : struct, INumber<TProp> =>
    ApplyPercent(column);

  public static GridColumn<TEntity, TRow, TProp?> AsPercent<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column)
    where TProp : struct, INumber<TProp> =>
    ApplyPercent(column);

  public static GridColumn<TEntity, TRow, TProp> Sum<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column)
    where TProp : struct, INumber<TProp> =>
    ApplySum(column);

  public static GridColumn<TEntity, TRow, TProp?> Sum<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column)
    where TProp : struct, INumber<TProp> =>
    ApplySum(column);

  public static GridColumn<TEntity, TRow, TProp> SelectionSum<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    string unit = "",
    string? excludeColor = null)
    where TProp : struct, INumber<TProp> =>
    column.SelectionSummary(SummaryKind.Sum, unit, excludeColor);

  public static GridColumn<TEntity, TRow, TProp?> SelectionSum<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    string unit = "",
    string? excludeColor = null)
    where TProp : struct, INumber<TProp> =>
    column.SelectionSummary(SummaryKind.Sum, unit, excludeColor);

  public static GridColumn<TEntity, TRow, TProp> Stepper<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    bool hasStepper = true)
    where TProp : struct, INumber<TProp> =>
    ApplyStepper(column, hasStepper);

  public static GridColumn<TEntity, TRow, TProp?> Stepper<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    bool hasStepper = true)
    where TProp : struct, INumber<TProp> =>
    ApplyStepper(column, hasStepper);

  public static GridColumn<TEntity, TRow, TProp> Step<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    double step)
    where TProp : struct, INumber<TProp> =>
    ApplyStep(column, step);

  public static GridColumn<TEntity, TRow, TProp?> Step<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    double step)
    where TProp : struct, INumber<TProp> =>
    ApplyStep(column, step);

  public static GridColumn<TEntity, TRow, TProp> Min<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    double min)
    where TProp : struct, INumber<TProp> =>
    ApplyMin(column, min);

  public static GridColumn<TEntity, TRow, TProp?> Min<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    double min)
    where TProp : struct, INumber<TProp> =>
    ApplyMin(column, min);

  public static GridColumn<TEntity, TRow, TProp> Max<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp> column,
    double max)
    where TProp : struct, INumber<TProp> =>
    ApplyMax(column, max);

  public static GridColumn<TEntity, TRow, TProp?> Max<TEntity, TRow, TProp>(
    this GridColumn<TEntity, TRow, TProp?> column,
    double max)
    where TProp : struct, INumber<TProp> =>
    ApplyMax(column, max);

  private static GridColumn<TEntity, TRow, T> ApplyNumber<TEntity, TRow, T>(GridColumn<TEntity, TRow, T> column)
  {
    column.Dto.Type = UiFieldType.Number;
    column.Dto.Cell = new GridCellNumberModel();
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyDecimals<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    int decimals)
  {
    switch (column.Dto.Cell)
    {
      case GridCellNumberModel num:
        num.Decimals = decimals;
        break;
      case GridCellMoneyModel money:
        money.Decimals = decimals;
        break;
      case GridCellPercentModel pct:
        pct.Decimals = decimals;
        break;
      default:
        column.Dto.Type = UiFieldType.Number;
        column.Dto.Cell = new GridCellNumberModel { Decimals = decimals };
        break;
    }

    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyGrouped<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    bool isGrouped)
  {
    if (column.Dto.Cell is GridCellNumberModel num)
    {
      num.IsGrouped = isGrouped;
    }
    else
    {
      column.Dto.Type = UiFieldType.Number;
      column.Dto.Cell = new GridCellNumberModel { IsGrouped = isGrouped };
    }

    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyMoney<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    string? currency)
  {
    column.Dto.Type = UiFieldType.Number;
    column.Dto.Cell = new GridCellMoneyModel { Currency = currency, Decimals = 2 };
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyPercent<TEntity, TRow, T>(GridColumn<TEntity, TRow, T> column)
  {
    column.Dto.Type = UiFieldType.Number;
    column.Dto.Cell = new GridCellPercentModel { Decimals = 2 };
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplySum<TEntity, TRow, T>(GridColumn<TEntity, TRow, T> column)
  {
    GridColumnRuntime.Of(column.Dto).IsSummed = true;
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyStepper<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    bool hasStepper)
  {
    column.Dto.NumberEdit ??= new GridNumberEditDto();
    column.Dto.NumberEdit.HasStepper = hasStepper;
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyStep<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    double step)
  {
    column.Dto.NumberEdit ??= new GridNumberEditDto();
    column.Dto.NumberEdit.Step = step;
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyMin<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    double min)
  {
    column.Dto.NumberEdit ??= new GridNumberEditDto();
    column.Dto.NumberEdit.Min = min;
    return column;
  }

  private static GridColumn<TEntity, TRow, T> ApplyMax<TEntity, TRow, T>(
    GridColumn<TEntity, TRow, T> column,
    double max)
  {
    column.Dto.NumberEdit ??= new GridNumberEditDto();
    column.Dto.NumberEdit.Max = max;
    return column;
  }
}
