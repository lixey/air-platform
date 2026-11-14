namespace Platform.App.Form.Actions;

public class FormActions<TEntity>
  where TEntity : class, IEntity
{
  public List<FormActionItem<TEntity>> Items { get; } = [];
  internal List<FormActionItem<TEntity>> Entries => Items;

  public FormActionItem<TEntity>? Find(string handle) =>
    Items.FirstOrDefault(a => a.Matches(handle));

  public bool Remove(string handle) =>
    Items.RemoveAll(a => a.Matches(handle)) > 0;

  public void Clear() => Items.Clear();

  public FormActionGate<TEntity> Add(string handle)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(handle);
    return new FormActionGate<TEntity>(this, handle);
  }
}
