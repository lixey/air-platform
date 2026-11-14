namespace Platform.App.Grid.Actions;

public class GridActions<TEntity>
  where TEntity : class, IEntity
{
  public List<GridActionItem<TEntity>> Items { get; } = [];
  internal List<GridActionItem<TEntity>> Entries => Items;
  internal List<Type> InheritedFormServiceTypes { get; } = [];

  public GridActionItem<TEntity>? Find(string handle) =>
    Items.FirstOrDefault(a => a.Matches(handle));

  public bool Remove(string handle) =>
    Items.RemoveAll(a => a.Matches(handle)) > 0;

  public void Clear()
  {
    Items.Clear();
    InheritedFormServiceTypes.Clear();
  }

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
