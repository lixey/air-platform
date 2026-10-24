namespace Platform.App.Form;

public class Elements<TEntity> : Elements where TEntity : class
{
  public FormElement<TEntity, TProp> Add<TProp>(Expression<Func<TEntity, TProp>> expression) =>
    Add<TEntity, TProp>(expression);

  public StatusFlowBuilder UseStatusOf(Screens.IRecordStatuses<TEntity> statuses) =>
    base.UseStatusOf(statuses);
}
