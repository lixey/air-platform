using Platform.App.Grid.Internals;

namespace Platform.App.Grid.Actions;

public class GridActions<TEntity>
  where TEntity : class, IEntity
{
  internal List<GridActionEntry<TEntity>> Entries { get; } = [];
  internal List<Type> InheritedFormServiceTypes { get; } = [];

  public GridActionGate<TEntity> Add(string handle)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(handle);
    return new GridActionGate<TEntity>(this, handle);
  }

  public void UseActionsOf<TFormService>()
    where TFormService : class, IFormService
  {
    if (!InheritedFormServiceTypes.Contains(typeof(TFormService)))
    {
      InheritedFormServiceTypes.Add(typeof(TFormService));
    }
  }
}
