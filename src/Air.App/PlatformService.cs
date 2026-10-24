namespace Air.App;

/// <summary>
/// Universal marker and base contract for any platform-managed service (Grid, Form, Report, Screen, or custom domain/RPC service).
/// </summary>
public interface IPlatformService
{
  Feature Feature { get; }
  void AttachPlatformApp(PlatformAppService app) { }
}

/// <summary>
/// Base class for custom domain, workflow, or integration services bound to a <see cref="Modules.Feature"/> and automatically exposed at <c>/api/{module}/{service}/{method}</c>.
/// </summary>
public abstract class PlatformService(PlatformAppService? app = null) : IPlatformService
{
  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext? User => PlatformApp?.User;
  protected ITenantContext? TenantContext => PlatformApp?.Tenant;
  protected ILocalizer L => Localizer.Instance;

  public virtual Feature Feature => Feature.FeaturesByServiceType.TryGetValue(GetType(), out var f)
    ? f
    : new Feature("", GetType().Name.Replace("Service", "").ToLowerInvariant(), GetType());

  public virtual void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;
}
