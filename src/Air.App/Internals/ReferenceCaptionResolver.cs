namespace Platform.App.Internals;

internal static class ReferenceCaptionResolver
{
  private static readonly MethodInfo LoadEntitiesByIdsGenericMethod =
    typeof(ReferenceCaptionResolver).GetMethod(nameof(LoadEntitiesByIdsCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  public static string? ReadDisplayCaption(
    object? target,
    IReadOnlyList<string>? labelPaths = null,
    IGridService? gridService = null)
  {
    if (target == null)
    {
      return null;
    }

    if (target is Reference refObj)
    {
      return refObj.Name;
    }

    var effectiveLabelPaths = labelPaths is { Count: > 0 }
      ? labelPaths
      : gridService?.GetReferenceLabelPaths();

    if (effectiveLabelPaths is { Count: > 0 })
    {
      var parts = new List<string>(effectiveLabelPaths.Count);
      foreach (var path in effectiveLabelPaths)
      {
        var rawPart = PropertyAccessorCache.GetPathValue(target, path);
        if (rawPart is null or Reference or IEntity)
        {
          continue;
        }

        var partVal = rawPart.ToString();
        if (!string.IsNullOrWhiteSpace(partVal))
        {
          parts.Add(partVal);
        }
      }

      if (parts.Count > 0)
      {
        return string.Join(" ", parts);
      }
    }

    if (gridService?.GetRecordLabel(target) is { Length: > 0 } recordLabel)
    {
      return recordLabel;
    }

    return PropertyAccessorCache.GetReferenceLabel(target);
  }

  public static async Task<Dictionary<long, object>> LoadEntitiesByIdsAsync(
    DbContext targetDb,
    Type entityType,
    IReadOnlyList<long> ids,
    IReadOnlyList<string>? labelPaths,
    IGridService? gridService,
    CancellationToken ct)
  {
    if (ids.Count == 0 || !typeof(IEntity).IsAssignableFrom(entityType))
    {
      return [];
    }

    var includePaths = CollectIncludePaths(entityType, labelPaths, gridService);
    try
    {
      var task = (Task<Dictionary<long, object>>)LoadEntitiesByIdsGenericMethod
        .MakeGenericMethod(entityType)
        .Invoke(null, [targetDb, ids, includePaths, ct])!;
      return await task;
    }
    catch
    {
      return [];
    }
  }

  private static IReadOnlyList<string> CollectIncludePaths(
    Type entityType,
    IReadOnlyList<string>? labelPaths,
    IGridService? gridService)
  {
    var includes = new HashSet<string>(StringComparer.Ordinal);
    var effectivePaths = labelPaths is { Count: > 0 }
      ? labelPaths
      : gridService?.GetReferenceLabelPaths();

    if (effectivePaths is { Count: > 0 })
    {
      foreach (var path in effectivePaths)
      {
        if (ResolveNavigationPrefix(entityType, path) is { Length: > 0 } navPrefix)
        {
          includes.Add(navPrefix);
        }
      }
    }

    if (gridService?.GetLabelNavigationPaths() is { Count: > 0 } labelNavs)
    {
      foreach (var nav in labelNavs)
      {
        if (!string.IsNullOrWhiteSpace(nav))
        {
          includes.Add(nav);
        }
      }
    }

    return includes.Count > 0 ? [.. includes] : [];
  }

  private static string? ResolveNavigationPrefix(Type rootType, string dotPath)
  {
    if (!dotPath.Contains('.'))
    {
      return null;
    }

    var segments = dotPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length < 2)
    {
      return null;
    }

    var navParts = new List<string>(segments.Length - 1);
    var currentType = rootType;
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var prop = PropertyAccessorCache.GetProperty(currentType, segments[i]);
      if (prop == null)
      {
        return null;
      }

      navParts.Add(prop.Name);
      currentType = prop.PropertyType;
    }

    return string.Join(".", navParts);
  }

  private static async Task<Dictionary<long, object>> LoadEntitiesByIdsCoreAsync<TRefEntity>(
    DbContext targetDb,
    IReadOnlyList<long> ids,
    IReadOnlyList<string> includePaths,
    CancellationToken ct)
    where TRefEntity : class, IEntity
  {
    var query = targetDb.Set<TRefEntity>().AsNoTracking();
    foreach (var nav in includePaths)
    {
      query = query.Include(nav);
    }

    var rows = await query
      .Where(e => ids.Contains(e.Id))
      .ToListAsync(ct);
    return rows.ToDictionary(r => r.Id, r => (object)r);
  }
}
