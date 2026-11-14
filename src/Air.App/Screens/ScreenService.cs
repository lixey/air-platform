using Platform.App.Internals;

namespace Platform.App.Screens;

public abstract class ScreenService(PlatformAppService? app = null) : PlatformService(app), IScreenService
{
  protected internal ScreenPanels Panels { get; private set; } = new(string.Empty);

  protected virtual void SetPanels() { }

  protected virtual Task SetPanelsAsync(CancellationToken cancellationToken = default)
  {
    SetPanels();
    return Task.CompletedTask;
  }

  public virtual async Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default)
  {
    var effectivePath = !string.IsNullOrWhiteSpace(servicePath)
      ? servicePath
      : ServicePathResolver.Resolve(GetType());
    Panels = new ScreenPanels(effectivePath);
    var panels = Panels;
    await SetPanelsAsync(ct);
    return new ScreenDto { Layout = panels.Build(L) };
  }

  [Endpoint(Auth = EndpointAuth.View)]
  public virtual async Task<ScreenDto> GetData(HttpRequest httpRequest, CancellationToken ct = default)
  {
    var serviceName = ExtractRouteServicePath(httpRequest) ?? ServicePathResolver.Resolve(GetType());
    var screen = await BuildScreenAsync(serviceName, ct);
    EnforceAndFilterScreen(GetType(), screen, User, Catalog);
    return screen;
  }

  internal static string? ExtractRouteServicePath(HttpRequest? httpRequest)
  {
    var rawPath = httpRequest?.Path.Value;
    if (string.IsNullOrEmpty(rawPath) || !rawPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    var servicePath = rawPath["/api/".Length..].Trim('/');
    var lastSlash = servicePath.LastIndexOf('/');
    return lastSlash > 0 ? servicePath[..lastSlash] : null;
  }

  void IPlatformService.ValidateConfiguration(ModuleCatalog catalog)
  {
    var screenType = GetType();
    ServiceOverrideValidator.EnsureSingleSyncOrAsyncOverride(
      screenType,
      typeof(ScreenService),
      (nameof(SetPanels), nameof(SetPanelsAsync)));

    if (ServiceOverrideFlags.For(screenType).OverridesSetPanelsAsync)
    {
      return;
    }

    ScreenDto screen;
    try
    {
      var panels = new ScreenPanels(ServicePathResolver.Resolve(screenType));
      Panels = panels;
      SetPanels();
      screen = new ScreenDto { Layout = panels.Build(L) };
    }
    catch when (PlatformApp is null)
    {
      return;
    }

    EnforceAndFilterScreen(screenType, screen, user: null, catalog);
  }

  public async Task RegisterMetadataAsync(CancellationToken ct = default)
  {
    var screenType = GetType();
    if (!ServiceOverrideFlags.For(screenType).OverridesSetPanelsAsync)
    {
      return;
    }

    ScreenDto screen;
    try
    {
      screen = await BuildScreenAsync(ServicePathResolver.Resolve(screenType), ct);
    }
    catch when (PlatformApp is null)
    {
      return;
    }

    EnforceAndFilterScreen(screenType, screen, user: null, Catalog);
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
