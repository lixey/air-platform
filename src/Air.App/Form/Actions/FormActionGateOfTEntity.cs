using Platform.App.Screens.Internals;

namespace Platform.App.Form.Actions;

public sealed class FormActionGate<TEntity>
  where TEntity : class, IEntity
{
  private readonly FormActions<TEntity> _owner;
  private readonly string _handle;

  internal FormActionGate(FormActions<TEntity> owner, string handle)
  {
    _owner = owner;
    _handle = handle;
  }

  public FormActionBuilder<TEntity> Allow(Right right) => Register(ActionPermission.FromRight(right));

  public FormActionBuilder<TEntity> Allow(Feature feature, string actionName) =>
    Register(ActionPermission.FromFeatureAction(feature, actionName));

  public FormActionBuilder<TEntity> AllowAction(string? actionName = null) =>
    Register(ActionPermission.FromOwnAction(actionName));

  public FormActionBuilder<TEntity> AllowEdit() => Register(ActionPermission.FromEdit());

  public FormActionBuilder<TEntity> AllowView() => Register(ActionPermission.FromView());

  private FormActionBuilder<TEntity> Register(ActionPermission permission)
  {
    var entry = new FormActionItem<TEntity>(_handle, permission);
    _owner.Items.Add(entry);
    return new FormActionBuilder<TEntity>(entry);
  }
}
