namespace Platform.App.Internals;

internal static class ServiceRegistry
{
  private static readonly ConcurrentDictionary<Type, IReadOnlyList<string>?> ReferenceLabelPathsCache = new();

  public static IReadOnlyList<string>? GetReferenceLabelPaths(
    Type gridServiceType,
    PlatformDbContext? db,
    PlatformAppService? app)
  {
    if (ReferenceLabelPathsCache.TryGetValue(gridServiceType, out var cached))
    {
      return cached is { Count: > 0 } ? cached : null;
    }

    if (!typeof(IGridService).IsAssignableFrom(gridServiceType))
    {
      ReferenceLabelPathsCache[gridServiceType] = [];
      return null;
    }

    try
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(gridServiceType, db, app) is IGridService grid)
      {
        var paths = grid.GetReferenceLabelPaths();
        if (paths is { Count: > 0 })
        {
          IReadOnlyList<string> snapshot = [.. paths];
          ReferenceLabelPathsCache[gridServiceType] = snapshot;
          return snapshot;
        }

        ReferenceLabelPathsCache[gridServiceType] = [];
      }
    }
    catch
    {
      // Service required runtime dependencies not available during probe; retry on next request.
    }

    return null;
  }
}
