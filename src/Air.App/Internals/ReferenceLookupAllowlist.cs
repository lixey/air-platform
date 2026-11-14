using System.Runtime.CompilerServices;

namespace Platform.App.Internals;

internal static class ReferenceLookupAllowlist
{
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedLabels = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedPatches = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedFilters = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, byte>> AllowedReferrers = new();
  private static readonly ConditionalWeakTable<ModuleCatalog, StrongBox<(int ServiceCount, Type[] ScopedServiceTypes, ConcurrentDictionary<Type, byte> WarmedAsyncTypes)>> StaticCatalogStates = new();

  public static async Task EnsureWarmAsync(IServiceProvider? services, CancellationToken ct = default)
  {
    var catalog = ModuleCatalog.Instance;
    var (scopedServiceTypes, warmedAsyncTypes) = EnsureStaticAllowlist(catalog);
    if (services == null || scopedServiceTypes.Length == 0 || warmedAsyncTypes.Count >= scopedServiceTypes.Length)
    {
      return;
    }

    var appInstance = services.GetService<PlatformAppService>();
    foreach (var serviceType in scopedServiceTypes)
    {
      if (warmedAsyncTypes.ContainsKey(serviceType))
      {
        continue;
      }

      var dbContextType = ModuleCatalog.GetServiceDbContextType(serviceType);
      if (dbContextType != null && services.GetService(dbContextType) == null)
      {
        continue;
      }

      try
      {
        if (typeof(Form.IFormService).IsAssignableFrom(serviceType))
        {
          if (catalog.CreateServiceInstance(serviceType, services, appInstance) is Form.IFormService form)
          {
            await form.RegisterMetadataAsync(ct);
            warmedAsyncTypes[serviceType] = 1;
          }
        }
        else if (typeof(Grid.IGridService).IsAssignableFrom(serviceType) &&
                 catalog.CreateServiceInstance(serviceType, services, appInstance) is Grid.IGridService grid)
        {
          await grid.RegisterMetadataAsync(ct);
          warmedAsyncTypes[serviceType] = 1;
        }
        else if (typeof(Screens.ScreenService).IsAssignableFrom(serviceType) &&
                 catalog.CreateServiceInstance(serviceType, services, appInstance) is Screens.ScreenService screen)
        {
          await screen.RegisterMetadataAsync(ct);
          warmedAsyncTypes[serviceType] = 1;
        }
      }
      catch (ParentNotLoadedException)
      {
        warmedAsyncTypes[serviceType] = 1;
      }
      catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
      {
      }
    }
  }

  public static void RegisterLabels(Type? serviceType, IEnumerable<string> labelPaths)
  {
    if (serviceType == null)
    {
      return;
    }

    var set = AllowedLabels.GetOrAdd(serviceType, static _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
    foreach (var path in labelPaths)
    {
      if (!string.IsNullOrWhiteSpace(path))
      {
        set[path] = 1;
      }
    }
  }

  public static void RegisterPatchColumn(Type? serviceType, string? column)
  {
    if (serviceType == null || string.IsNullOrWhiteSpace(column))
    {
      return;
    }

    var set = AllowedPatches.GetOrAdd(serviceType, static _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
    set[column] = 1;
  }

  public static void RegisterFilterField(Type? serviceType, string? field)
  {
    if (serviceType == null || string.IsNullOrWhiteSpace(field))
    {
      return;
    }

    var set = AllowedFilters.GetOrAdd(serviceType, static _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
    set[field] = 1;
  }

  public static void RegisterReferrer(Type? targetServiceType, Type? referrerServiceType)
  {
    if (targetServiceType == null || referrerServiceType == null || targetServiceType == referrerServiceType)
    {
      return;
    }

    var set = AllowedReferrers.GetOrAdd(targetServiceType, static _ => new ConcurrentDictionary<Type, byte>());
    set[referrerServiceType] = 1;
  }

  public static bool CanUserLookup(Type targetServiceType, ModuleCatalog catalog, IUserContext user)
  {
    _ = EnsureStaticAllowlist(catalog);

    if (!AllowedReferrers.TryGetValue(targetServiceType, out var referrers) || referrers.IsEmpty)
    {
      return false;
    }

    foreach (var referrerType in referrers.Keys)
    {
      var referrerFeature = catalog.FindFeature(referrerType);
      if (referrerFeature != null && ServiceEndpointsExtensions.CanView(referrerFeature, user))
      {
        return true;
      }
    }

    return false;
  }

  private static (Type[] ScopedServiceTypes, ConcurrentDictionary<Type, byte> WarmedAsyncTypes) EnsureStaticAllowlist(ModuleCatalog catalog)
  {
    var box = StaticCatalogStates.GetValue(catalog, static _ => new StrongBox<(int, Type[], ConcurrentDictionary<Type, byte>)>((-1, [], new())));
    var count = catalog.ServiceCount;
    var snapshot = box.Value;
    if (snapshot.ServiceCount == count)
    {
      return (snapshot.ScopedServiceTypes, snapshot.WarmedAsyncTypes);
    }

    lock (box)
    {
      count = catalog.ServiceCount;
      if (box.Value.ServiceCount == count)
      {
        return (box.Value.ScopedServiceTypes, box.Value.WarmedAsyncTypes);
      }

      var scopedTypes = new List<Type>();
      foreach (var serviceType in catalog.AllServiceTypes)
      {
        var flags = ServiceOverrideFlags.For(serviceType);
        if (flags.HasAsyncFormMetadata || flags.HasAsyncGridMetadata || flags.OverridesSetPanelsAsync)
        {
          scopedTypes.Add(serviceType);
        }

        var probe = catalog.CreateServiceInstance(serviceType);
        if (probe is Screens.IRecordStatuses flow)
        {
          flow.RegisterMetadata();
        }
        else if (probe is IPlatformService ps)
        {
          try
          {
            ps.ValidateConfiguration(catalog);
          }
          catch (ParentNotLoadedException)
          {
            if (!scopedTypes.Contains(serviceType))
            {
              scopedTypes.Add(serviceType);
            }
          }
        }
      }

      var built = scopedTypes.ToArray();
      var warmed = new ConcurrentDictionary<Type, byte>();
      box.Value = (count, built, warmed);
      return (built, warmed);
    }
  }

  public static IReadOnlyCollection<string> GetAllowedFilterFields(Type? serviceType) =>
    serviceType != null && AllowedFilters.TryGetValue(serviceType, out var set)
      ? [.. set.Keys]
      : [];

  public static string GetAllowedFilterSignature(Type? serviceType) =>
    serviceType != null && AllowedFilters.TryGetValue(serviceType, out var set) && !set.IsEmpty
      ? string.Join(',', set.Keys.OrderBy(static k => k, StringComparer.OrdinalIgnoreCase))
      : string.Empty;

  public static List<string>? FilterLabelPaths(
    Type serviceType,
    IReadOnlyList<string>? gridDefaultLabels,
    IReadOnlyList<string>? requested)
  {
    if (requested is not { Count: > 0 })
    {
      return gridDefaultLabels is { Count: > 0 } ? [.. gridDefaultLabels] : ["name"];
    }

    AllowedLabels.TryGetValue(serviceType, out var registered);
    var result = requested
      .Where(p =>
        string.Equals(p, "name", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(p, "id", StringComparison.OrdinalIgnoreCase) ||
        gridDefaultLabels != null && gridDefaultLabels.Contains(p, StringComparer.OrdinalIgnoreCase) ||
        registered != null && registered.ContainsKey(p))
      .ToList();

    return result.Count > 0 ? result : ["name"];
  }

  public static List<string>? FilterPatchColumns(Type serviceType, IReadOnlyList<string>? requested)
  {
    if (requested is not { Count: > 0 } || !AllowedPatches.TryGetValue(serviceType, out var registered))
    {
      return null;
    }

    var result = requested.Where(registered.ContainsKey).ToList();
    return result.Count > 0 ? result : null;
  }

  public static bool TrySanitizeFilter(Type serviceType, FilterNode? filter, out FilterNode? sanitized)
  {
    if (filter == null)
    {
      sanitized = null;
      return true;
    }

    AllowedFilters.TryGetValue(serviceType, out var registered);
    if (!IsAllowedNode(filter, registered))
    {
      sanitized = null;
      return false;
    }

    sanitized = filter;
    return true;
  }

  private static bool IsAllowedNode(FilterNode node, ConcurrentDictionary<string, byte>? registered)
  {
    if (node is FilterRule rule)
    {
      if (string.IsNullOrWhiteSpace(rule.Field))
      {
        return false;
      }

      var isAllowedField = string.Equals(rule.Field, "id", StringComparison.OrdinalIgnoreCase) ||
                           registered != null && registered.ContainsKey(rule.Field);
      var isAllowedOp = rule.Operator is
        FilterOperator.Equals or
        FilterOperator.NotEquals or
        FilterOperator.IsNull or
        FilterOperator.IsNotNull or
        FilterOperator.In or
        FilterOperator.NotIn or
        FilterOperator.GreaterThan or
        FilterOperator.GreaterThanOrEqual or
        FilterOperator.LessThan or
        FilterOperator.LessThanOrEqual;
      return isAllowedField && isAllowedOp;
    }

    if (node is FilterGroup group)
    {
      return group.Items.All(child => IsAllowedNode(child, registered));
    }

    return false;
  }
}
