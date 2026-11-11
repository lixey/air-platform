using Platform.App.Form.Internals;

namespace Platform.App.Form.Actions;

public class FormActions<TEntity>
  where TEntity : class, IEntity
{
  internal List<FormActionEntry<TEntity>> Entries { get; } = [];

  public FormActionGate<TEntity> Add(string handle)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(handle);
    return new FormActionGate<TEntity>(this, handle);
  }
}
