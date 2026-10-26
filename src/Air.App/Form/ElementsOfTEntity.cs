namespace Platform.App.Form;

public class Elements<TEntity> : Elements where TEntity : class
{
  public FormElement<TEntity, TProp> Add<TProp>(Expression<Func<TEntity, TProp>> expression) =>
    Add<TEntity, TProp>(expression);

  public FormElement<TEntity, TProp> AddInfo<TProp>(string label, Expression<Func<TEntity, TProp>> expression, string? key = null) =>
    AddInfo<TEntity, TProp>(label, expression, key);

  public StatusFlowBuilder UseStatusOf(Screens.IRecordStatuses<TEntity> statuses) =>
    base.UseStatusOf(statuses);
}
