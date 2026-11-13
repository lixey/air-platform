namespace Platform.Api.Modules;

public sealed class ModuleCatalog
{
  private static readonly ConcurrentDictionary<Type, Type?> ServiceDbContextCache = new();
  private static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();

  public static ModuleCatalog Instance { get; set; } = new();

  private readonly Lock _sync = new();
  private readonly Dictionary<string, Feature> _featuresByPath = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, Feature> _featuresByKey = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<Type, Feature> _featuresByServiceType = [];
  private readonly Dictionary<string, Type> _servicesByPath = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<IPlatformModule> _modules = [];
  private readonly List<Type> _dbContextTypes = [];
  private volatile FrozenDictionary<string, Feature>? _frozenFeaturesByPath;
  private volatile FrozenDictionary<string, Feature>? _frozenFeaturesByKey;
  private volatile FrozenDictionary<Type, Feature>? _frozenFeaturesByServiceType;
  private volatile FrozenDictionary<string, Type>? _frozenServicesByPath;

  public bool IsFrozen { get; private set; }
  public IReadOnlyList<IPlatformModule> Modules => _modules;
  public IReadOnlyList<Type> DbContextTypes => _dbContextTypes;
  public int ServiceCount => _frozenServicesByPath?.Count ?? _servicesByPath.Count;
  public IEnumerable<Feature> AllFeatures => _frozenFeaturesByKey?.Values ?? (IEnumerable<Feature>)_featuresByKey.Values;
  public IEnumerable<Type> AllServiceTypes => (_frozenServicesByPath?.Values ?? (IEnumerable<Type>)_servicesByPath.Values).Distinct();

  public ModuleCatalog Freeze()
  {
    lock (_sync)
    {
      if (!IsFrozen)
      {
        _frozenFeaturesByPath = _featuresByPath.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _frozenFeaturesByKey = _featuresByKey.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _frozenFeaturesByServiceType = _featuresByServiceType.ToFrozenDictionary();
        _frozenServicesByPath = _servicesByPath.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        IsFrozen = true;
      }

      return this;
    }
  }

  public void Register(IPlatformModule module)
  {
    ArgumentNullException.ThrowIfNull(module);
    lock (_sync)
    {
      if (_modules.Any(m => m.GetType() == module.GetType()))
      {
        return;
      }

      if (IsFrozen)
      {
        throw new InvalidOperationException(
          $"ModuleCatalog is frozen and cannot register new module '{module.GetType().Name}' after host startup.");
      }

      CodeRows.EnsurePascalCaseHandle(module.Handle, $"Module '{module.GetType().Name}'");
      if (_modules.Any(m => string.Equals(m.Handle, module.Handle, StringComparison.OrdinalIgnoreCase)))
      {
        throw new InvalidOperationException(
          $"Duplicate module handle '{module.Handle}' registered by '{module.GetType().Name}'.");
      }

      _modules.Add(module);

      var moduleFeatures = new List<Feature>();
      foreach (var feat in module.Features)
      {
        RegisterFeature(feat);
        moduleFeatures.Add(feat);
      }

      foreach (var node in Flatten(module.Menu.Nodes))
      {
        if (node.Feature is not null)
        {
          RegisterFeature(node.Feature);
          if (!moduleFeatures.Contains(node.Feature))
          {
            moduleFeatures.Add(node.Feature);
          }
        }
      }

      var assemblyTypes = module.GetType().Assembly.GetTypes();
      foreach (var t in assemblyTypes)
      {
        if (t is { IsClass: true, IsAbstract: false } && GetServiceDbContextType(t) is { IsAbstract: false } ctxType && !_dbContextTypes.Contains(ctxType))
        {
          _dbContextTypes.Add(ctxType);
        }
      }

      var knownHandles = _modules.Select(m => m.Handle).ToArray();
      var serviceTypes = GetModuleServiceTypes(module, knownHandles);

      foreach (var st in serviceTypes)
      {
        var path = ServicePathResolver.Resolve(st, knownHandles);
        if (_servicesByPath.TryGetValue(path, out var existingService) && existingService != st)
        {
          throw new InvalidOperationException(
            $"Duplicate canonical service route '{path}': both '{existingService.FullName}' and '{st.FullName}' map to the same path.");
        }

        _servicesByPath[path] = st;

        if (CreateServiceInstance(st) is IPlatformService ps)
        {
          var svcFeat = ps.Feature;
          if (svcFeat is not null)
          {
            _featuresByServiceType[st] = svcFeat;
            DiscoverAndRegisterServiceActions(st, ps, svcFeat);
          }
        }
      }

      foreach (var feat in moduleFeatures)
      {
        feat.Freeze();
      }
    }
  }

  private static void DiscoverAndRegisterServiceActions(Type serviceType, IPlatformService serviceInstance, Feature feature)
  {
    foreach (var method in serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
    {
      if (method.GetCustomAttribute<RequireActionAttribute>(inherit: true) is { FallbackToView: false } reqAct &&
          !Right.StandardActions.Contains(reqAct.ActionName) &&
          !feature.HasAction(reqAct.ActionName))
      {
        if (feature.IsFrozen)
        {
          throw new InvalidOperationException(
            $"Service '{serviceType.FullName}' cannot add custom action '{reqAct.ActionName}' to already-frozen feature '{feature.Key}'. Declare the right on '{feature.Key}' in its owning module.");
        }

        feature.Action(reqAct.ActionName);
      }
    }

    foreach (var declared in serviceInstance.GetDeclaredActions())
    {
      if (!string.IsNullOrWhiteSpace(declared) &&
          !Right.StandardActions.Contains(declared) &&
          !feature.HasAction(declared))
      {
        if (feature.IsFrozen)
        {
          throw new InvalidOperationException(
            $"Service '{serviceType.FullName}' cannot add flow/custom action '{declared}' to already-frozen feature '{feature.Key}'. Declare the right on '{feature.Key}' in its owning module.");
        }

        feature.Action(declared);
      }
    }
  }

  private void RegisterFeature(Feature feat)
  {
    ArgumentNullException.ThrowIfNull(feat);
    if (_featuresByKey.TryGetValue(feat.Key, out var existingByKey) && !ReferenceEquals(existingByKey, feat))
    {
      throw new InvalidOperationException($"Duplicate feature key '{feat.Key}'.");
    }

    if (_featuresByPath.TryGetValue(feat.Path, out var existingByPath) && !ReferenceEquals(existingByPath, feat))
    {
      throw new InvalidOperationException(
        $"Duplicate feature path '{feat.Path}' between '{existingByPath.Key}' and '{feat.Key}'.");
    }

    _featuresByKey[feat.Key] = feat;
    _featuresByPath[feat.Path] = feat;

    if (feat.ServiceType != null)
    {
      _featuresByServiceType[feat.ServiceType] = feat;
      var servicePath = ServicePathResolver.Resolve(feat.ServiceType);
      _featuresByPath[servicePath] = feat;

      if (_servicesByPath.TryGetValue(feat.Path, out var existingSvc) && existingSvc != feat.ServiceType)
      {
        throw new InvalidOperationException(
          $"Duplicate feature service route '{feat.Path}': '{existingSvc.FullName}' conflicts with '{feat.ServiceType.FullName}'.");
      }

      _servicesByPath[feat.Path] = feat.ServiceType;
    }
  }

  public Feature? FindFeature(string? path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    var normalized = path.Trim('/');
    if (_frozenFeaturesByPath is { } frozenByPath)
    {
      if (frozenByPath.TryGetValue(normalized, out var frozenFeat))
      {
        return frozenFeat;
      }
    }
    else if (_featuresByPath.TryGetValue(normalized, out var feat))
    {
      return feat;
    }

    return FindServiceType(normalized) is { } serviceType
      ? FindFeature(serviceType)
      : null;
  }

  public Feature? FindFeature(Type? serviceType) =>
    serviceType != null &&
    (_frozenFeaturesByServiceType is { } frozen
      ? frozen.TryGetValue(serviceType, out var feat)
      : _featuresByServiceType.TryGetValue(serviceType, out feat))
      ? feat
      : null;

  public Feature? FindFeatureForEntity(Type? entityType, string? tableName = null)
  {
    if (entityType != null)
    {
      foreach (var feat in AllFeatures)
      {
        if (feat.ServiceType != null && GetServiceEntityType(feat.ServiceType) == entityType)
        {
          return feat;
        }
      }

      foreach (var kv in _featuresByServiceType)
      {
        if (GetServiceEntityType(kv.Key) == entityType)
        {
          return kv.Value;
        }
      }
    }

    if (!string.IsNullOrWhiteSpace(tableName))
    {
      return AllFeatures.FirstOrDefault(f =>
        string.Equals(f.Handle, tableName, StringComparison.OrdinalIgnoreCase));
    }

    return null;
  }

  public static Type? GetServiceEntityType(Type? serviceType)
  {
    var cur = serviceType?.BaseType;
    while (cur != null && cur != typeof(object))
    {
      if (cur.IsGenericType &&
          cur.GetGenericArguments() is [var firstArg, ..] &&
          typeof(IEntity).IsAssignableFrom(firstArg) &&
          !firstArg.IsAbstract)
      {
        return firstArg;
      }

      cur = cur.BaseType;
    }

    return null;
  }

  public static Type? GetServiceDbContextType(Type? serviceType) =>
    serviceType is null
      ? null
      : ServiceDbContextCache.GetOrAdd(serviceType, static t =>
      {
        var ctorParams = t.GetConstructors()
          .FirstOrDefault()
          ?.GetParameters();
        if (ctorParams is null)
        {
          return null;
        }

        foreach (var p in ctorParams)
        {
          if (typeof(PlatformDbContext).IsAssignableFrom(p.ParameterType))
          {
            return p.ParameterType;
          }
        }

        foreach (var p in ctorParams)
        {
          if (p.ParameterType != typeof(PlatformAppService) &&
              typeof(PlatformAppService).IsAssignableFrom(p.ParameterType))
          {
            var appCtorParams = p.ParameterType.GetConstructors().FirstOrDefault()?.GetParameters();
            var appDb = appCtorParams?.FirstOrDefault(ap => typeof(PlatformDbContext).IsAssignableFrom(ap.ParameterType))?.ParameterType;
            if (appDb is not null)
            {
              return appDb;
            }
          }
        }

        return null;
      });

  private Type? ResolveModuleDbContextType(Type? serviceType)
  {
    var moduleHandle = FindFeature(serviceType)?.Module;
    if (string.IsNullOrEmpty(moduleHandle))
    {
      return null;
    }

    Type? resolved = null;
    foreach (var (st, feat) in _featuresByServiceType)
    {
      if (!string.Equals(feat.Module, moduleHandle, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      var candidate = GetServiceDbContextType(st);
      if (candidate is null || candidate == typeof(PlatformDbContext))
      {
        continue;
      }

      if (resolved is null || resolved.IsAssignableFrom(candidate))
      {
        resolved = candidate;
      }
    }

    return resolved;
  }

  public static void EnsureModuleServiceAccessAllowed(
    Type sourceServiceType,
    Type? targetServiceType,
    string context,
    ModuleCatalog? catalog = null)
  {
    if (targetServiceType is null)
    {
      return;
    }

    var activeCatalog = catalog ?? Instance;
    var sourceDbType = GetServiceDbContextType(sourceServiceType) ?? activeCatalog.ResolveModuleDbContextType(sourceServiceType);
    var targetDbType = GetServiceDbContextType(targetServiceType) ?? activeCatalog.ResolveModuleDbContextType(targetServiceType);
    if (sourceDbType is not null &&
        targetDbType is not null &&
        sourceDbType != typeof(PlatformDbContext) &&
        targetDbType != typeof(PlatformDbContext) &&
        !targetDbType.IsAssignableFrom(sourceDbType))
    {
      throw new InvalidOperationException(
        $"Module isolation violation in '{sourceServiceType.Name}' ({context}): cannot reference service '{targetServiceType.Name}' from non-base module '{targetDbType.Name}' (source context is '{sourceDbType.Name}'). Services may only reference services from their own module or base modules they inherit from.");
    }
  }

  public Type? FindServiceType(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    var key = path.Trim('/');
    return _frozenServicesByPath is { } frozen
      ? frozen.GetValueOrDefault(key)
      : _servicesByPath.GetValueOrDefault(key);
  }

  public object? CreateServiceInstance(Type serviceType, params object?[] availableServices) =>
    CreateServiceInstanceCore(serviceType, availableServices, []);

  private object? CreateServiceInstanceCore(Type serviceType, object?[] availableServices, HashSet<Type> visited)
  {
    if (visited.Count >= 8 || !visited.Add(serviceType))
    {
      return null;
    }

    try
    {
      if (serviceType.IsAbstract)
      {
        var candidates = AllServiceTypes
          .Where(t => !t.IsAbstract && serviceType.IsAssignableFrom(t))
          .ToList();
        var canonicalName = serviceType.Name.EndsWith("Base", StringComparison.Ordinal)
          ? serviceType.Name[..^4]
          : null;
        var resolved = candidates.Count == 1
          ? candidates[0]
          : canonicalName != null
            ? candidates.FirstOrDefault(t => string.Equals(t.Name, canonicalName, StringComparison.Ordinal))
            : null;
        if (resolved is null || resolved != serviceType && !visited.Add(resolved))
        {
          return null;
        }

        serviceType = resolved;
      }

      var ctor = serviceType.GetConstructors().FirstOrDefault();
      if (ctor is null)
      {
        return null;
      }

      var serviceProvider = availableServices.OfType<IServiceProvider>().FirstOrDefault()
        ?? availableServices.OfType<PlatformAppService>().FirstOrDefault()?.Services;
      var appService = availableServices.OfType<PlatformAppService>().FirstOrDefault()
        ?? serviceProvider?.GetService(typeof(PlatformAppService)) as PlatformAppService;

      var parameters = ctor.GetParameters();
      var args = new object?[parameters.Length];
      for (var i = 0; i < parameters.Length; i++)
      {
        var param = parameters[i];
        var match = availableServices.FirstOrDefault(s => s is not null && param.ParameterType.IsInstanceOfType(s))
          ?? serviceProvider?.GetService(param.ParameterType);
        if (match is not null)
        {
          args[i] = match;
        }
        else if (param.HasDefaultValue)
        {
          args[i] = param.DefaultValue;
        }
        else if (param.ParameterType.IsValueType)
        {
          args[i] = Activator.CreateInstance(param.ParameterType);
        }
        else if (param.ParameterType == typeof(IUserContext))
        {
          args[i] = new UserContext(new HttpContextAccessor());
        }
        else if (param.ParameterType == typeof(ITenantContext))
        {
          args[i] = AsyncLocalTenantContext.Instance;
        }
        else if (param.ParameterType == typeof(ILocalizer))
        {
          args[i] = Localizer.Instance;
        }
        else if (param.ParameterType == typeof(IServiceProvider))
        {
          args[i] = EmptyServiceProvider;
        }
        else if (param.ParameterType == typeof(TimeProvider))
        {
          args[i] = TimeProvider.System;
        }
        else if (!param.ParameterType.IsInterface &&
                 !param.ParameterType.IsAbstract &&
                 param.ParameterType != typeof(string) &&
                 !typeof(DbContext).IsAssignableFrom(param.ParameterType) &&
                 !visited.Contains(param.ParameterType))
        {
          args[i] = CreateServiceInstanceCore(param.ParameterType, availableServices, visited);
        }
      }

      var instance = ctor.Invoke(args);
      if (appService is not null && instance is IPlatformService platformService)
      {
        platformService.AttachPlatformApp(appService);
      }

      return instance;
    }
    finally
    {
      visited.Remove(serviceType);
    }
  }

  internal static IReadOnlyList<Type> GetModuleServiceTypes(IPlatformModule module, IEnumerable<string>? additionalHandles = null)
  {
    var assemblyTypes = module.GetType().Assembly.GetTypes();
    var moduleCountInAssembly = assemblyTypes.Count(t =>
      t is { IsClass: true, IsAbstract: false } && typeof(IPlatformModule).IsAssignableFrom(t));

    var allServices = assemblyTypes
      .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlatformService).IsAssignableFrom(t));

    if (moduleCountInAssembly <= 1)
    {
      return [.. allServices];
    }

    var prefix = $"{ServicePathResolver.ToKebabCase(module.Handle)}/";
    var handles = (additionalHandles ?? []).Append(module.Handle).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    var declaredServiceTypes = module.Features
      .Concat(Flatten(module.Menu.Nodes).Select(n => n.Feature).OfType<Feature>())
      .Select(f => f.ServiceType)
      .OfType<Type>()
      .ToHashSet();

    return [.. allServices.Where(t =>
      declaredServiceTypes.Contains(t) ||
      ServicePathResolver.Resolve(t, handles).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
  }

  public static IEnumerable<ModuleMenuNode> Flatten(IEnumerable<ModuleMenuNode> nodes)
  {
    foreach (var node in nodes)
    {
      yield return node;
      foreach (var child in Flatten(node.Children))
      {
        yield return child;
      }
    }
  }
}
