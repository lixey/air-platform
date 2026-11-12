namespace Platform.App.Internals;

internal static class ReferenceLookupAllowlist
{
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedLabels = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedPatches = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedFilters = new();
  private static readonly SemaphoreSlim WarmSemaphore = new(1, 1);
  private static ModuleCatalog? _warmedCatalog;

  public static async Task EnsureWarmAsync(IServiceProvider? services, CancellationToken ct = default)
  {
    var catalog = ModuleCatalog.Instance;
    if (ReferenceEquals(_warmedCatalog, catalog) || services == null)
    {
      return;
    }

    await WarmSemaphore.WaitAsync(ct);
    try
    {
      catalog = ModuleCatalog.Instance;
      if (ReferenceEquals(_warmedCatalog, catalog))
      {
        return;
      }

      var appInstance = services.GetService<PlatformAppService>();
      foreach (var serviceType in catalog.AllServiceTypes)
      {
        try
        {
          if (typeof(Form.IFormService).IsAssignableFrom(serviceType))
          {
            if (catalog.CreateServiceInstance(serviceType, services, appInstance) is Form.IFormService form)
            {
              await form.RegisterMetadataAsync(ct);
            }
          }
          else if (typeof(Grid.IGridService).IsAssignableFrom(serviceType))
          {
            if (catalog.CreateServiceInstance(serviceType, services, appInstance) is Grid.IGridService grid)
            {
              await grid.RegisterMetadataAsync(ct);
            }
          }
          else if (typeof(Screens.IRecordStatuses).IsAssignableFrom(serviceType))
          {
            if (catalog.CreateServiceInstance(serviceType, services, appInstance) is Screens.IRecordStatuses flow)
            {
              flow.RegisterMetadata();
            }
          }
        }
        catch
        {
        }
      }

      _warmedCatalog = catalog;
    }
    finally
    {
      WarmSemaphore.Release();
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
