namespace Platform.App.Screens.Internals;

internal sealed class ActionPermission
{
  private static readonly Regex HandlePattern = new("^[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);

  private readonly int _kind;
  private readonly Right? _right;
  private readonly Feature? _targetFeature;
  private readonly string? _actionName;

  private ActionPermission(int kind, Right? right = null, Feature? targetFeature = null, string? actionName = null)
  {
    _kind = kind;
    _right = right;
    _targetFeature = targetFeature;
    _actionName = actionName;
  }

  public static ActionPermission FromRight(Right right)
  {
    ArgumentNullException.ThrowIfNull(right);
    return new ActionPermission(1, right: right);
  }

  public static ActionPermission FromFeatureAction(Feature targetFeature, string actionName)
  {
    ArgumentNullException.ThrowIfNull(targetFeature);
    ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
    return new ActionPermission(2, targetFeature: targetFeature, actionName: actionName);
  }

  public static ActionPermission FromOwnAction(string? customActionName = null) =>
    new(3, actionName: string.IsNullOrWhiteSpace(customActionName) ? null : customActionName);

  public static ActionPermission FromEdit() => new(4);

  public static ActionPermission FromView() => new(5);

  public static void ValidateHandle(string handle, Type hostServiceType)
  {
    if (string.IsNullOrWhiteSpace(handle) || !HandlePattern.IsMatch(handle))
    {
      throw new InvalidOperationException(
        $"Action handle '{handle}' in '{hostServiceType.FullName}' must be a PascalCase identifier (^[A-Z][a-zA-Z0-9]*$).");
    }
  }

  public string? GetDeclaredActionName(string handle) =>
    _kind == 3 ? (_actionName ?? handle) : null;

  public void ValidateConfiguration(Type hostServiceType, Feature hostFeature, string handle)
  {
    ValidateHandle(handle, hostServiceType);
    switch (_kind)
    {
      case 1 when _right?.Feature?.ServiceType is { } rightServiceType:
        ModuleCatalog.EnsureModuleServiceAccessAllowed(hostServiceType, rightServiceType, $"Actions.Allow on '{handle}'");
        break;

      case 2 when _targetFeature != null:
        if (_targetFeature.ServiceType is { } featServiceType)
        {
          ModuleCatalog.EnsureModuleServiceAccessAllowed(hostServiceType, featServiceType, $"Actions.Allow on '{handle}'");
        }

        if (!_targetFeature.TryGetRight(_actionName!, out _) &&
            !string.Equals(_actionName, Right.View, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_actionName, Right.Edit, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_actionName, Right.Delete, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(_actionName, Right.Export, StringComparison.OrdinalIgnoreCase))
        {
          throw new InvalidOperationException(
            $"Action '{handle}' in '{hostServiceType.FullName}' references undeclared right '{_actionName}' on feature '{_targetFeature.Name}'.");
        }

        break;

      case 4 when hostFeature.IsReadOnly:
        throw new InvalidOperationException(
          $"Action '{handle}' in '{hostServiceType.FullName}' uses AllowEdit() on read-only feature '{hostFeature.Name}'. Use AllowView(), Allow(Right), or AllowAction(...) instead.");
    }
  }

  public bool IsPermitted(
    Feature hostFeature,
    IUserContext? user,
    string handle,
    bool isScreenReadOnly,
    bool isAllowedInReadOnly,
    bool isParentReadOnly = false)
  {
    if (user is not { IsAuthenticated: true })
    {
      return false;
    }

    if (isParentReadOnly && !isAllowedInReadOnly)
    {
      return false;
    }

    if (isScreenReadOnly && !isAllowedInReadOnly)
    {
      var hasDedicatedRight = _kind is 1 or 2 or 3 or 5;
      if (!hasDedicatedRight)
      {
        return false;
      }
    }

    return _kind switch
    {
      1 => user.Can(_right!) || _right!.Feature != null && user.Can(_right.Feature.All) || user.Can("*"),
      2 => CheckFeatureAction(_targetFeature!, _actionName!, user),
      3 => CheckFeatureAction(hostFeature, _actionName ?? handle, user),
      4 => ServiceEndpointsExtensions.CanEdit(hostFeature, user),
      5 => ServiceEndpointsExtensions.CanView(hostFeature, user),
      _ => false
    };
  }

  private static bool CheckFeatureAction(Feature feature, string actionName, IUserContext user)
  {
    if (string.Equals(actionName, Right.View, StringComparison.OrdinalIgnoreCase))
    {
      return ServiceEndpointsExtensions.CanView(feature, user);
    }

    if (string.Equals(actionName, Right.Edit, StringComparison.OrdinalIgnoreCase))
    {
      return ServiceEndpointsExtensions.CanEdit(feature, user);
    }

    if (string.Equals(actionName, Right.Delete, StringComparison.OrdinalIgnoreCase))
    {
      return feature.HasDelete && (user.Can(feature.Delete) || user.Can(feature.All) || user.Can("*"));
    }

    if (string.Equals(actionName, Right.Export, StringComparison.OrdinalIgnoreCase))
    {
      return ServiceEndpointsExtensions.CanExport(feature, user);
    }

    return feature.TryGetRight(actionName, out var right) && user.Can(right) ||
           user.Can(feature.All) ||
           user.Can("*");
  }
}
