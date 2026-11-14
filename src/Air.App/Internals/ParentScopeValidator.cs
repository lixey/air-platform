namespace Platform.App.Internals;

internal static class ParentScopeValidator
{
  private static readonly ConcurrentDictionary<(ModuleCatalog Catalog, Type GridServiceType), Type?> CachedConventionFormTypes = new();

  public static Type? ResolveConventionFormType(Type? gridServiceType)
  {
    if (gridServiceType == null)
    {
      return null;
    }

    var catalog = ModuleCatalog.Instance;
    return CachedConventionFormTypes.GetOrAdd((catalog, gridServiceType), static key =>
    {
      var (cat, gridType) = key;
      var entityType = GetGridEntityType(gridType);
      if (entityType == null)
      {
        return null;
      }

      var candidateForms = cat.AllServiceTypes
        .Where(t => !t.IsAbstract && typeof(Form.IFormService).IsAssignableFrom(t) && GetFormEntityType(t) == entityType)
        .ToList();
      if (candidateForms.Count == 0)
      {
        return null;
      }

      var gridFeature = cat.FindFeature(gridType);
      if (gridFeature != null)
      {
        var featureMatches = candidateForms
          .Where(t => ReferenceEquals(cat.FindFeature(t), gridFeature))
          .ToList();
        if (featureMatches.Count == 1)
        {
          return featureMatches[0];
        }

        if (featureMatches.Count > 1)
        {
          var sameNs = featureMatches.Where(t => t.Namespace == gridType.Namespace).ToList();
          return sameNs.Count == 1 ? sameNs[0] : featureMatches[0];
        }

        return null;
      }

      var nsMatches = candidateForms.Where(t => t.Namespace == gridType.Namespace).ToList();
      if (nsMatches.Count == 1)
      {
        return nsMatches[0];
      }

      var asmMatches = candidateForms.Where(t => t.Assembly == gridType.Assembly).ToList();
      return asmMatches.Count == 1 ? asmMatches[0] : null;
    });
  }

  public static bool DeclaresParentScope(Type? serviceType)
  {
    var setParentMethod = serviceType?.GetMethod("SetParent", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);
    if (setParentMethod?.DeclaringType is not { } declaring)
    {
      return false;
    }

    if (!declaring.IsGenericType)
    {
      return true;
    }

    var def = declaring.GetGenericTypeDefinition();
    return def != typeof(Form.FormService<>) && def != typeof(Grid.GridService<,>);
  }

  public static Type? GetFormEntityType(Type? type)
  {
    for (var t = type; t != null && t != typeof(object); t = t.BaseType)
    {
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Form.FormService<>))
      {
        return t.GetGenericArguments()[0];
      }
    }

    return null;
  }

  public static Type? GetGridEntityType(Type? type)
  {
    for (var t = type; t != null && t != typeof(object); t = t.BaseType)
    {
      if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Grid.GridService<,>))
      {
        return t.GetGenericArguments()[0];
      }
    }

    return null;
  }

  public static bool MatchesService(string? parentService, string? boundParentService, Type? boundParentServiceType)
  {
    if (string.IsNullOrEmpty(parentService) || string.IsNullOrEmpty(boundParentService))
    {
      return true;
    }

    if (string.Equals(parentService, boundParentService, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (boundParentServiceType != null &&
        ModuleCatalog.Instance.FindServiceType(parentService) is { } actualType)
    {
      return boundParentServiceType.IsAssignableFrom(actualType) ||
             actualType.IsAssignableFrom(boundParentServiceType);
    }

    return false;
  }

  public static TParent? GetParentRecord<TParent>(
    PlatformDbContext db,
    long? parentId,
    string? parentService,
    object? preloadedParent,
    long? preloadedParentId,
    Type? boundParentServiceType)
    where TParent : class, IEntity
  {
    if (parentId is not > 0)
    {
      return null;
    }

    if (preloadedParent is TParent preloaded && preloaded.Id == parentId.Value)
    {
      return preloaded;
    }

    if (preloadedParentId == parentId.Value &&
        (boundParentServiceType != null || !string.IsNullOrEmpty(parentService)))
    {
      return null;
    }

    return db.Set<TParent>().Local.FirstOrDefault(e => e.Id == parentId.Value);
  }

  public static async Task<(bool Allowed, object? PreloadedParent, IReadOnlyList<Form.ChildGridBuilder>? ParentChildren)> ValidateAndLoadAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    long? parentId,
    string? requestParentService,
    bool hasRequiredBoundParent,
    Func<string?, bool> matchesService,
    string? boundParentService,
    Type? boundParentServiceType,
    Type? boundParentEntityType,
    bool lockParent,
    CancellationToken ct)
  {
    if (parentId.HasValue && parentId.Value <= 0)
    {
      return (false, null, null);
    }

    if (hasRequiredBoundParent && parentId is null)
    {
      return (false, null, null);
    }

    var effectiveParentService = !string.IsNullOrEmpty(requestParentService)
      ? requestParentService
      : boundParentService;
    if (!string.IsNullOrEmpty(effectiveParentService) && !matchesService(effectiveParentService))
    {
      return (false, null, null);
    }

    Type? parentServiceType = null;
    if (!string.IsNullOrEmpty(requestParentService))
    {
      parentServiceType = ModuleCatalog.Instance.FindServiceType(requestParentService);
      if (parentServiceType == null &&
          !(boundParentServiceType is { IsAbstract: true } &&
            string.Equals(requestParentService, boundParentService, StringComparison.OrdinalIgnoreCase)))
      {
        return (false, null, null);
      }
    }

    parentServiceType ??= boundParentServiceType;

    if (parentId is null)
    {
      if (parentServiceType is { IsAbstract: false } &&
          typeof(Form.IFormService).IsAssignableFrom(parentServiceType) &&
          ModuleCatalog.Instance.CreateServiceInstance(parentServiceType, db, app) is Form.IFormService metadataForm)
      {
        metadataForm.SetRequest(new Form.Contracts.FormRequest());
        var (_, children) = await metadataForm.GetElementsMetadataAsync(ct);
        return (true, null, children);
      }

      return (true, null, null);
    }

    var parentEntityType = ModuleCatalog.GetServiceEntityType(parentServiceType) ?? boundParentEntityType;

    if (parentServiceType != null && typeof(Form.IFormService).IsAssignableFrom(parentServiceType))
    {
      var candidateTypes = parentServiceType.IsAbstract
        ? ModuleCatalog.Instance.AllServiceTypes
          .Where(t => !t.IsAbstract && parentServiceType.IsAssignableFrom(t))
          .ToList()
        : [parentServiceType];

      var viewableForms = new List<Form.IFormService>(candidateTypes.Count);
      foreach (var candidateType in candidateTypes)
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(candidateType, db, app) is not Form.IFormService candidateForm)
        {
          continue;
        }

        if (!string.IsNullOrEmpty(candidateForm.Feature.Module) &&
            (app?.User is not { IsAuthenticated: true } user ||
             !ServiceEndpointsExtensions.CanView(candidateForm.Feature, user)))
        {
          continue;
        }

        viewableForms.Add(candidateForm);
      }

      if (viewableForms.Count == 0)
      {
        return (false, null, null);
      }

      if (lockParent && parentEntityType != null && db.Model.FindEntityType(parentEntityType) != null)
      {
        await MutationContext.AcquireLocksAsync(db, parentEntityType, [[parentId.Value]], ct);
      }

      foreach (var candidateForm in viewableForms)
      {
        candidateForm.SetRequest(new Form.Contracts.FormRequest { Id = parentId });
        var loaded = await candidateForm.LoadParentRecordAsync(parentId.Value, ct);
        if (loaded != null)
        {
          var (_, children) = await candidateForm.GetElementsMetadataAsync(ct);
          return (true, loaded, children);
        }
      }

      return (false, null, null);
    }

    if (parentServiceType != null && typeof(Grid.IGridService).IsAssignableFrom(parentServiceType))
    {
      var candidateTypes = parentServiceType.IsAbstract
        ? ModuleCatalog.Instance.AllServiceTypes
          .Where(t => !t.IsAbstract && parentServiceType.IsAssignableFrom(t))
          .ToList()
        : [parentServiceType];

      var viewableGrids = new List<Grid.IGridService>(candidateTypes.Count);
      foreach (var candidateType in candidateTypes)
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(candidateType, db, app) is not Grid.IGridService candidateGrid)
        {
          continue;
        }

        if (!string.IsNullOrEmpty(candidateGrid.Feature.Module) &&
            !candidateGrid.CanView(new Grid.Contracts.GridRequest(), app?.User))
        {
          continue;
        }

        viewableGrids.Add(candidateGrid);
      }

      if (viewableGrids.Count == 0)
      {
        return (false, null, null);
      }

      if (lockParent && parentEntityType != null && db.Model.FindEntityType(parentEntityType) != null)
      {
        await MutationContext.AcquireLocksAsync(db, parentEntityType, [[parentId.Value]], ct);
      }

      foreach (var candidateGrid in viewableGrids)
      {
        if (await candidateGrid.ReadRowAsync(parentId.Value, ct) != null)
        {
          var loaded = parentEntityType != null && db.Model.FindEntityType(parentEntityType) != null
            ? await db.FindAsync(parentEntityType, [parentId.Value], ct)
            : null;
          return (true, loaded, null);
        }
      }

      return (false, null, null);
    }

    if (parentEntityType != null && db.Model.FindEntityType(parentEntityType) != null)
    {
      if (lockParent)
      {
        await MutationContext.AcquireLocksAsync(db, parentEntityType, [[parentId.Value]], ct);
      }

      var loaded = await db.FindAsync(parentEntityType, [parentId.Value], ct);
      return loaded != null ? (true, loaded, null) : (false, null, null);
    }

    return (false, null, null);
  }

  public static async Task<Form.ChildGridBuilder?> ResolveChildBuilderAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    string? requestParentService,
    string? boundParentService,
    Type? boundParentServiceType,
    long? parentId,
    Action<(string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)> setCache,
    (string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)? cache,
    Func<Form.ChildGridBuilder, bool> predicate,
    CancellationToken ct = default)
  {
    var effectiveParentService = !string.IsNullOrEmpty(requestParentService)
      ? requestParentService
      : boundParentService;
    if (string.IsNullOrEmpty(effectiveParentService) && boundParentServiceType == null)
    {
      return null;
    }

    IReadOnlyList<Form.ChildGridBuilder> children;
    if (cache is { } cached &&
        string.Equals(cached.Service, effectiveParentService, StringComparison.OrdinalIgnoreCase) &&
        cached.ParentId == parentId)
    {
      children = cached.Children;
    }
    else
    {
      children = [];
      var parentFormType = (!string.IsNullOrEmpty(effectiveParentService)
        ? ModuleCatalog.Instance.FindServiceType(effectiveParentService)
        : null) ?? boundParentServiceType;
      var parentForm = await BoundParentRecalculator.ResolveConcreteParentFormAsync(db, app, parentFormType, parentId, ct);
      if (parentForm != null)
      {
        parentForm.SetRequest(new Form.Contracts.FormRequest { Id = parentId });
        (_, children) = await parentForm.GetElementsMetadataAsync(ct);
      }

      setCache((effectiveParentService, parentId, children));
    }

    return children.FirstOrDefault(predicate);
  }

  public static Form.ChildGridBuilder? ResolveChildBuilder(
    string? requestParentService,
    string? boundParentService,
    long? parentId,
    (string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)? cache,
    Func<Form.ChildGridBuilder, bool> predicate)
  {
    var effectiveParentService = !string.IsNullOrEmpty(requestParentService)
      ? requestParentService
      : boundParentService;
    if (string.IsNullOrEmpty(effectiveParentService))
    {
      return null;
    }

    if (cache is { } cached &&
        string.Equals(cached.Service, effectiveParentService, StringComparison.OrdinalIgnoreCase) &&
        cached.ParentId == parentId)
    {
      return cached.Children.FirstOrDefault(predicate);
    }

    return null;
  }
}
