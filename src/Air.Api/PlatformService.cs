namespace Platform.Api;

/// <summary>
/// Base class for domain, workflow, or platform services bound to a <see cref="Modules.Feature"/> and exposed via <see cref="EndpointAttribute"/>.
/// </summary>
public abstract class PlatformService(PlatformAppService? app = null) : IPlatformService
{
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext? User => PlatformApp?.User;
  protected ITenantContext? TenantContext => PlatformApp?.Tenant;
  protected ModuleCatalog Catalog => PlatformApp?.Catalog ?? ModuleCatalog.Instance;
  protected ICultureCatalog Cultures => PlatformApp?.Cultures ?? CultureCatalog.DefaultInstance;
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;
  protected ILocalizer L => (PlatformApp?.L ?? Localizer.Instance).For(Feature?.Module);

  public abstract Feature Feature { get; }
  void IPlatformService.BindContext(PlatformAppService? service) => PlatformApp ??= service;
}
