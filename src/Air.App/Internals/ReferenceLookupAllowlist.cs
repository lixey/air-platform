namespace Platform.App.Internals;

internal static class ReferenceLookupAllowlist
{
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedLabels = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedPatches = new();
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> AllowedFilters = new();

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

  public static List<string>? FilterLabelPaths(
    Type serviceType,
    IReadOnlyList<string>? gridDefaultLabels,
    IReadOnlyList<string>? requested)
  {
    if (requested is not { Count: > 0 })
    {
      return null;
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

  public static FilterNode? SanitizeFilter(Type serviceType, FilterNode? filter)
  {
    if (filter == null)
    {
      return null;
    }

    AllowedFilters.TryGetValue(serviceType, out var registered);
    return SanitizeNode(filter, registered);
  }

  private static FilterNode? SanitizeNode(FilterNode node, ConcurrentDictionary<string, byte>? registered)
  {
    if (node is FilterRule rule)
    {
      var isAllowedField = string.Equals(rule.Field, "id", StringComparison.OrdinalIgnoreCase) ||
                           registered != null && registered.ContainsKey(rule.Field);
      var isAllowedOp = rule.Operator is FilterOperator.Equals or FilterOperator.IsNull or FilterOperator.In;
      return isAllowedField && isAllowedOp ? rule : null;
    }

    if (node is FilterGroup group)
    {
      var items = group.Items
        .Select(child => SanitizeNode(child, registered))
        .Where(child => child != null)
        .Cast<FilterNode>()
        .ToList();
      return items.Count > 0 ? new FilterGroup { Logic = group.Logic, Items = items } : null;
    }

    return null;
  }
}
