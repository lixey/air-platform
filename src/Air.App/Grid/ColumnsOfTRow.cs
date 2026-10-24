namespace Platform.App.Grid;

public class Columns<TRow> : Columns where TRow : class
{
  public GridColumn<TRow, TProp> Add<TProp>(Expression<Func<TRow, TProp>> expression)
  {
    var baseCol = base.Add<TRow, TProp>(expression);
    return new GridColumn<TRow, TProp>(baseCol.Dto, this);
  }

  public StatusFlowBuilder UseStatusOf(Screens.IRecordStatuses<TRow> statuses) =>
    base.UseStatusOf(statuses);
}
