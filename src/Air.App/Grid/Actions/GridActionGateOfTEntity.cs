using Platform.App.Grid.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Grid.Actions;

public sealed class GridActionGate<TEntity>
  where TEntity : class, IEntity
{
  private readonly GridActions<TEntity> _owner;
  private readonly string _handle;

  internal GridActionGate(GridActions<TEntity> owner, string handle)
  {
    _owner = owner;
    _handle = handle;
  }

  public GridActionBuilder<TEntity> Allow(Right right) => Register(ActionPermission.FromRight(right));

  public GridActionBuilder<TEntity> Allow(Feature feature, string actionName) =>
    Register(ActionPermission.FromFeatureAction(feature, actionName));

  public GridActionBuilder<TEntity> AllowAction(string? actionName = null) =>
    Register(ActionPermission.FromOwnAction(actionName));

  public GridActionBuilder<TEntity> AllowEdit() => Register(ActionPermission.FromEdit());

  public GridActionBuilder<TEntity> AllowView() => Register(ActionPermission.FromView());

  private GridActionBuilder<TEntity> Register(ActionPermission permission)
  {
    var entry = new GridActionEntry<TEntity>(_handle, permission);
    _owner.Entries.Add(entry);
    return new GridActionBuilder<TEntity>(entry);
  }
}
