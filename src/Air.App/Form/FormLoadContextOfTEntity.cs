namespace Platform.App.Form;

public class FormLoadContext<TEntity>
{
  public required TEntity Entity { get; init; }
  public bool IsNew { get; init; }
}
