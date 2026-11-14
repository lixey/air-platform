namespace Platform.Api;

/// <summary>
/// Base class for domain, workflow, or platform services bound to a <see cref="Modules.Feature"/> and exposed via <see cref="EndpointAttribute"/>.
/// </summary>
public abstract class PlatformService(PlatformAppService? app = null) : IPlatformService
{
  private Dictionary<string, object?>? _executionItems;

  protected PlatformAppService? PlatformApp { get; private set; } = app;
  protected IUserContext? User => PlatformApp?.User;
  protected ITenantContext? TenantContext => PlatformApp?.Tenant;
  protected ModuleCatalog Catalog => PlatformApp?.Catalog ?? ModuleCatalog.Instance;
  protected ICultureCatalog Cultures => PlatformApp?.Cultures ?? CultureCatalog.DefaultInstance;
  protected TimeProvider Time => PlatformApp?.Time ?? PlatformAppService.UtcTime;
  protected ILocalizer L => (PlatformApp?.L ?? Localizer.Instance).For(Feature?.Module);
  protected IDictionary<string, object?> ExecutionItems => _executionItems ??= new Dictionary<string, object?>(StringComparer.Ordinal);

  public abstract Feature Feature { get; }

  protected T? GetExecutionItem<T>(string key) =>
    _executionItems is not null && _executionItems.TryGetValue(key, out var value) && value is T typed
      ? typed
      : default;

  protected void SetExecutionItem<T>(string key, T value) =>
    ExecutionItems[key] = value;

  protected T? GetExecutionItem<T>() =>
    GetExecutionItem<T>(typeof(T).FullName ?? typeof(T).Name);

  protected void SetExecutionItem<T>(T value) =>
    SetExecutionItem(typeof(T).FullName ?? typeof(T).Name, value);

  void IPlatformService.BindContext(PlatformAppService? service) => PlatformApp ??= service;
  void IPlatformService.ResetExecutionScope() => _executionItems = null;
}
