namespace Platform.App.Flow.Internals;

internal static class FlowFollowerRegistry
{
  private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, byte>> FollowersByChildFlow = new();
  private static (ModuleCatalog? Catalog, int ServiceCount) _warmedCatalog;
  private static readonly Lock WarmLock = new();

  public static void Register(Type childFlowType, Type parentFlowType)
  {
    if (childFlowType == parentFlowType)
    {
      return;
    }

    var bag = FollowersByChildFlow.GetOrAdd(childFlowType, static _ => new ConcurrentDictionary<Type, byte>());
    bag.TryAdd(parentFlowType, 0);
  }

  public static IReadOnlyList<Type> GetFollowers(
    Type childFlowType,
    IReadOnlyList<Type> explicitFollowers,
    PlatformDbContext db,
    PlatformAppService? app)
  {
    EnsureCatalogWarmed(db, app);

    var result = new List<Type>(explicitFollowers.Count + 2);
    var seen = new HashSet<Type>();
    foreach (var explicitType in explicitFollowers)
    {
      if (seen.Add(explicitType))
      {
        result.Add(explicitType);
      }
    }

    var childDbType = ModuleCatalog.GetServiceDbContextType(childFlowType);
    foreach (var (registeredChild, parentSet) in FollowersByChildFlow)
    {
      if (!registeredChild.IsAssignableFrom(childFlowType) && !childFlowType.IsAssignableFrom(registeredChild))
      {
        continue;
      }

      foreach (var parentType in parentSet.Keys)
      {
        if (parentType == childFlowType)
        {
          continue;
        }

        var parentDbType = ModuleCatalog.GetServiceDbContextType(parentType);
        if (childDbType != null &&
            parentDbType != null &&
            childDbType != typeof(PlatformDbContext) &&
            parentDbType != typeof(PlatformDbContext) &&
            !childDbType.IsAssignableFrom(parentDbType))
        {
          continue;
        }

        if (seen.Add(parentType))
        {
          result.Add(parentType);
        }
      }
    }

    return result;
  }

  private static void EnsureCatalogWarmed(PlatformDbContext db, PlatformAppService? app)
  {
    var catalog = ModuleCatalog.Instance;
    var serviceCount = catalog.ServiceCount;
    if (_warmedCatalog.Catalog == catalog && _warmedCatalog.ServiceCount == serviceCount)
    {
      return;
    }

    lock (WarmLock)
    {
      serviceCount = catalog.ServiceCount;
      if (_warmedCatalog.Catalog == catalog && _warmedCatalog.ServiceCount == serviceCount)
      {
        return;
      }

      foreach (var st in catalog.AllServiceTypes)
      {
        if (!typeof(IRecordStatuses).IsAssignableFrom(st) || st.IsAbstract)
        {
          continue;
        }

        if (catalog.CreateServiceInstance(st, db, app) is IRecordStatuses flow)
        {
          _ = flow.Statuses;
        }
      }

      _warmedCatalog = (catalog, serviceCount);
    }
  }
}
