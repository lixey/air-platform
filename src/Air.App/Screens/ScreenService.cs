namespace Platform.App.Screens;

public abstract class ScreenService(PlatformAppService? app = null) : PlatformService(app), IScreenService
{
  public abstract Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    ScreenDto? screen;
    try
    {
      screen = BuildScreenAsync(ServicePathResolver.Resolve(GetType())).GetAwaiter().GetResult();
    }
    catch when (PlatformApp is null)
    {
      return;
    }

    EnforceAndFilterScreen(GetType(), screen, user: null, catalog);
  }

  public static void EnforceAndFilterScreen(
    Type hostServiceType,
    ScreenDto screen,
    IUserContext? user,
    ModuleCatalog catalog)
  {
    if (screen.Layout?.Root is { } root)
    {
      _ = EnforceAndFilterNode(hostServiceType, root, user, catalog, isRoot: true);
    }
  }

  private static bool EnforceAndFilterNode(
    Type hostServiceType,
    PanelNodeDto node,
    IUserContext? user,
    ModuleCatalog catalog,
    bool isRoot)
  {
    var embeddedPath = node.Model is PanelScreenModel { Service.Length: > 0 } psm ? psm.Service : null;
    var targetType = node.EmbeddedServiceType
      ?? (embeddedPath is not null
        ? catalog.FindServiceType(embeddedPath) ?? catalog.FindFeature(embeddedPath)?.ServiceType
        : null);

    if (targetType is not null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(hostServiceType, targetType, $"Panel.Screen '{node.Key}'", catalog);
    }

    if (user is not null && (targetType is not null || embeddedPath is not null))
    {
      var targetFeat = (targetType is not null ? catalog.FindFeature(targetType) : null)
        ?? (embeddedPath is not null ? catalog.FindFeature(embeddedPath) : null);
      if (targetFeat is not null && !ServiceEndpointsExtensions.CanView(targetFeat, user))
      {
        return false;
      }
    }

    if (node.Children is not { Count: > 0 })
    {
      return true;
    }

    for (var i = node.Children.Count - 1; i >= 0; i--)
    {
      if (!EnforceAndFilterNode(hostServiceType, node.Children[i], user, catalog, isRoot: false))
      {
        node.Children.RemoveAt(i);
      }
    }

    return isRoot || node.Model is not null || node.Children.Count > 0;
  }
}
