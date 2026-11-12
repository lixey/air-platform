namespace Platform.Agent.Internals;

internal sealed class AgentModuleScope : IAsyncDisposable, IDisposable
{
  private readonly AsyncServiceScope? _childScope;
  private readonly CultureInfo? _prevCulture;
  private readonly CultureInfo? _prevUiCulture;
  private bool _disposed;

  private AgentModuleScope(
    AsyncServiceScope? childScope,
    PlatformDbContext db,
    PlatformAppService app,
    CultureInfo? prevCulture,
    CultureInfo? prevUiCulture)
  {
    _childScope = childScope;
    Db = db;
    App = app;
    _prevCulture = prevCulture;
    _prevUiCulture = prevUiCulture;
  }

  public PlatformDbContext Db { get; }
  public PlatformAppService App { get; }

  public static Type? ResolveTargetContextType(Type? serviceOrContextType)
  {
    if (serviceOrContextType == null)
    {
      return null;
    }

    if (typeof(PlatformDbContext).IsAssignableFrom(serviceOrContextType))
    {
      return serviceOrContextType;
    }

    var direct = ModuleCatalog.GetServiceDbContextType(serviceOrContextType);
    if (direct != null)
    {
      return direct;
    }

    var feat = ModuleCatalog.Instance.FindFeature(serviceOrContextType);
    return feat?.ServiceType != null
      ? ModuleCatalog.GetServiceDbContextType(feat.ServiceType)
      : null;
  }

  public static AgentModuleScope OpenForService(
    IServiceProvider rootServices,
    Type? serviceType,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null) =>
    OpenForContextType(rootServices, ResolveTargetContextType(serviceType), fallbackDb, fallbackApp);

  public static AgentModuleScope OpenForServicePath(
    IServiceProvider rootServices,
    string? servicePath,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null)
  {
    var serviceType = !string.IsNullOrWhiteSpace(servicePath)
      ? ModuleCatalog.Instance.FindServiceType(servicePath)
      : null;
    return OpenForService(rootServices, serviceType, fallbackDb, fallbackApp);
  }

  public static AgentModuleScope OpenDefault(
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null) =>
    OpenForContextType(rootServices, targetContextType: null, fallbackDb, fallbackApp);

  public static AgentModuleScope OpenForContextType(
    IServiceProvider rootServices,
    Type? targetContextType,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null)
  {
    ArgumentNullException.ThrowIfNull(rootServices);

    var mcpCtx = rootServices.GetService<McpExecutionBridge>()?.Active;
    CultureInfo? prevCulture = null;
    CultureInfo? prevUiCulture = null;
    if (mcpCtx?.Locale is { Length: > 0 } locale &&
        !string.Equals(CultureInfo.CurrentUICulture.Name, locale, StringComparison.OrdinalIgnoreCase))
    {
      try
      {
        var targetCulture = CultureInfo.GetCultureInfo(locale);
        prevCulture = CultureInfo.CurrentCulture;
        prevUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = targetCulture;
        CultureInfo.CurrentUICulture = targetCulture;
      }
      catch (CultureNotFoundException)
      {
        prevCulture = null;
        prevUiCulture = null;
      }
    }

    var hasDbScope = rootServices.GetService<DbScope>() != null;
    if (hasDbScope)
    {
      var childScope = rootServices.CreateAsyncScope();
      try
      {
        var sp = childScope.ServiceProvider;
        if (targetContextType != null)
        {
          sp.GetRequiredService<DbScope>().Bind(targetContextType);
        }

        var resolvedDb = targetContextType != null
          ? (PlatformDbContext)sp.GetRequiredService(targetContextType)
          : sp.GetRequiredService<PlatformDbContext>();

        var outerUser = fallbackApp?.User ?? sp.GetService<IUserContext>() ?? rootServices.GetService<IUserContext>();
        var wrappedUser = AgentScopedUserContext.Wrap(outerUser, mcpCtx);
        var tenant = fallbackApp?.Tenant ?? sp.GetService<ITenantContext>() ?? AsyncLocalTenantContext.Instance;
        var scopedAppService = new PlatformAppService(wrappedUser, sp, tenant, fallbackApp?.L);
        return new AgentModuleScope(childScope, resolvedDb, scopedAppService, prevCulture, prevUiCulture);
      }
      catch
      {
        childScope.Dispose();
        RestoreCulture(prevCulture, prevUiCulture);
        throw;
      }
    }

    var directDb = targetContextType != null
      ? rootServices.GetService(targetContextType) as PlatformDbContext
        ?? (fallbackDb != null && targetContextType.IsInstanceOfType(fallbackDb) ? fallbackDb : null)
        ?? throw new InvalidOperationException($"DbContext '{targetContextType.Name}' is not registered.")
      : fallbackDb ?? rootServices.GetRequiredService<PlatformDbContext>();
    var baseUser = fallbackApp?.User ?? rootServices.GetService<IUserContext>();
    var effectiveUser = AgentScopedUserContext.Wrap(baseUser, mcpCtx);
    var effectiveApp = fallbackApp != null && ReferenceEquals(effectiveUser, fallbackApp.User)
      ? fallbackApp
      : new PlatformAppService(
          effectiveUser,
          fallbackApp?.Services ?? rootServices,
          fallbackApp?.Tenant ?? rootServices.GetService<ITenantContext>() ?? AsyncLocalTenantContext.Instance,
          fallbackApp?.L);

    return new AgentModuleScope(null, directDb, effectiveApp, prevCulture, prevUiCulture);
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    RestoreCulture(_prevCulture, _prevUiCulture);
    _childScope?.Dispose();
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    RestoreCulture(_prevCulture, _prevUiCulture);
    if (_childScope is { } scope)
    {
      await scope.DisposeAsync();
    }
  }

  private static void RestoreCulture(CultureInfo? prevCulture, CultureInfo? prevUiCulture)
  {
    if (prevCulture != null)
    {
      CultureInfo.CurrentCulture = prevCulture;
    }

    if (prevUiCulture != null)
    {
      CultureInfo.CurrentUICulture = prevUiCulture;
    }
  }
}
