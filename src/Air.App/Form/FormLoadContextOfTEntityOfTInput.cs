namespace Platform.App.Form;

public class FormLoadContext<TEntity, TInput>
{
  public required TEntity Entity { get; init; }
  public required TInput Input { get; init; }
  public bool IsNew { get; init; }
}
