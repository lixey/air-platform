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
    var setParentMethod = serviceType?.GetMethod("SetParent", BindingFlags.Instance | BindingFlags.NonPublic);
    return setParentMethod is { DeclaringType.IsGenericType: false };
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

  public static PropertyInfo? ExtractBoundProperty<TEntity, TProp>(Expression<Func<TEntity, TProp>> member)
  {
    var expr = member.Body is UnaryExpression ue ? ue.Operand : member.Body;
    return expr is MemberExpression { Member: PropertyInfo pi } ? pi : null;
  }

  public static Expression<Func<TEntity, bool>> BuildParentKeyFilter<TEntity>(PropertyInfo prop, object parentVal)
  {
    var param = Expression.Parameter(typeof(TEntity), "e");
    var propExpr = Expression.Property(param, prop);
    var constExpr = Expression.Constant(parentVal, prop.PropertyType);
    return Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(propExpr, constExpr), param);
  }

  public static Expression<Func<TEntity, bool>> BuildKeyInFilter<TEntity>(PropertyInfo prop, IReadOnlyCollection<long> ids)
  {
    var param = Expression.Parameter(typeof(TEntity), "e");
    Expression propExpr = Expression.Property(param, prop);
    if (prop.PropertyType == typeof(long?))
    {
      propExpr = Expression.Coalesce(propExpr, Expression.Constant(0L));
    }

    return BuildKeyInLambda<TEntity>(param, propExpr, ids);
  }

  public static Expression<Func<TEntity, bool>> BuildKeyInFilter<TEntity>(
    Expression<Func<TEntity, long>> keySelector,
    IReadOnlyCollection<long> ids) =>
    BuildKeyInLambda<TEntity>(keySelector.Parameters[0], keySelector.Body, ids);

  private static Expression<Func<TEntity, bool>> BuildKeyInLambda<TEntity>(
    ParameterExpression param,
    Expression keyLongExpr,
    IReadOnlyCollection<long> ids)
  {
    var idList = ids as List<long> ?? [.. ids];
    var containsMethod = typeof(Enumerable).GetMethods()
      .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
      .MakeGenericMethod(typeof(long));
    var inListExpr = Expression.Call(containsMethod, Expression.Constant(idList), keyLongExpr);
    return Expression.Lambda<Func<TEntity, bool>>(inListExpr, param);
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
        if (app != null)
        {
          metadataForm.AttachPlatformApp(app);
        }

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

        if (app != null)
        {
          candidateForm.AttachPlatformApp(app);
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

        if (app != null)
        {
          candidateGrid.AttachPlatformApp(app);
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
      var parentForm = await ResolveConcreteParentFormAsync(db, app, parentFormType, parentId, ct);
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

  public static async Task<List<AffectedDto>?> RecalculateBoundParentsAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    string? requestParentService,
    string? boundParentService,
    Type? boundParentServiceType,
    IEnumerable<long?> parentIds,
    CancellationToken ct,
    long? requestParentId = null)
  {
    var distinctIds = parentIds
      .Where(id => id is > 0)
      .Select(id => id!.Value)
      .Distinct()
      .OrderBy(id => id)
      .ToList();
    if (distinctIds.Count == 0)
    {
      return null;
    }

    var explicitParentType = !string.IsNullOrEmpty(requestParentService)
      ? ModuleCatalog.Instance.FindServiceType(requestParentService)
      : null;
    var fallbackParentType = boundParentServiceType
      ?? (!string.IsNullOrEmpty(boundParentService) ? ModuleCatalog.Instance.FindServiceType(boundParentService) : null);
    var baseParentType = explicitParentType ?? fallbackParentType;
    if (baseParentType == null || !typeof(Form.IFormService).IsAssignableFrom(baseParentType))
    {
      return null;
    }

    var formsById = new List<(long Id, Form.IFormService Form)>(distinctIds.Count);
    if (!baseParentType.IsAbstract && (fallbackParentType == null || !fallbackParentType.IsAbstract || distinctIds.Count == 1 && distinctIds[0] == requestParentId))
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(baseParentType, db, app) is not Form.IFormService singleForm)
      {
        return null;
      }

      if (app != null)
      {
        singleForm.AttachPlatformApp(app);
      }

      foreach (var id in distinctIds)
      {
        formsById.Add((id, singleForm));
      }
    }
    else
    {
      var resolvedByType = new Dictionary<Type, Form.IFormService>();
      foreach (var id in distinctIds)
      {
        Form.IFormService? matchedForm = null;
        if (explicitParentType is { IsAbstract: false })
        {
          if (!resolvedByType.TryGetValue(explicitParentType, out var explicitForm) &&
              ModuleCatalog.Instance.CreateServiceInstance(explicitParentType, db, app) is Form.IFormService createdExplicit)
          {
            if (app != null)
            {
              createdExplicit.AttachPlatformApp(app);
            }

            resolvedByType[explicitParentType] = explicitForm = createdExplicit;
          }

          if (explicitForm != null)
          {
            explicitForm.SetRequest(new Form.Contracts.FormRequest { Id = id });
            if (await explicitForm.LoadParentRecordAsync(id, ct) != null)
            {
              matchedForm = explicitForm;
            }
          }
        }

        if (matchedForm == null && fallbackParentType != null)
        {
          matchedForm = await ResolveConcreteParentFormAsync(db, app, fallbackParentType, id, ct, resolvedByType);
        }

        if (matchedForm != null)
        {
          formsById.Add((id, matchedForm));
        }
      }
    }

    if (formsById.Count == 0)
    {
      return null;
    }

    foreach (var group in formsById.GroupBy(x => x.Form.GetType()))
    {
      var groupForm = group.First().Form;
      var idsForForm = group.Select(x => x.Id).ToList();
      if (await groupForm.NeedsParentRecalculationAsync(ct))
      {
        await db.RunDerivedRecalculationAsync(
          token => groupForm.RecalculateParentAsync(idsForForm, token),
          ct);
      }
    }

    if (distinctIds.Count > 5)
    {
      return null;
    }

    var parentEntityType = GetFormEntityType(baseParentType);
    List<AffectedDto>? affected = null;
    foreach (var (id, parentForm) in formsById)
    {
      if (!ServiceEndpointsExtensions.CanView(parentForm.Feature, app?.User))
      {
        continue;
      }

      var initialVer = parentEntityType != null ? db.GetInitialMutationVersion(parentEntityType, id) : null;
      if (initialVer == null && id != requestParentId)
      {
        continue;
      }

      parentForm.SetRequest(new Form.Contracts.FormRequest { Id = id });
      var loaded = await parentForm.LoadParentRecordAsync(id, ct);
      if (loaded == null)
      {
        continue;
      }

      var formData = await parentForm.BuildFormDataAsync(loaded, ct: ct);
      var prevVer = initialVer ?? formData.Ver;
      affected ??= [];
      affected.Add(new AffectedDto
      {
        Service = parentForm.Service,
        Id = id,
        PrevVer = prevVer,
        Ver = formData.Ver,
        Value = formData.Value,
        Unavailable = formData.Unavailable
      });
    }

    return affected;
  }

  private static async Task<Form.IFormService?> ResolveConcreteParentFormAsync(
    PlatformDbContext db,
    PlatformAppService? app,
    Type? parentFormType,
    long? parentId,
    CancellationToken ct,
    Dictionary<Type, Form.IFormService>? instanceCache = null)
  {
    if (parentFormType == null || !typeof(Form.IFormService).IsAssignableFrom(parentFormType))
    {
      return null;
    }

    if (!parentFormType.IsAbstract)
    {
      if (instanceCache != null && instanceCache.TryGetValue(parentFormType, out var cached))
      {
        return cached;
      }

      if (ModuleCatalog.Instance.CreateServiceInstance(parentFormType, db, app) is Form.IFormService direct)
      {
        if (app != null)
        {
          direct.AttachPlatformApp(app);
        }

        instanceCache?.TryAdd(parentFormType, direct);
        return direct;
      }

      return null;
    }

    var candidates = ModuleCatalog.Instance.AllServiceTypes
      .Where(t => !t.IsAbstract && parentFormType.IsAssignableFrom(t))
      .ToList();
    if (candidates.Count == 0)
    {
      return null;
    }

    if (parentId is not > 0)
    {
      return candidates.Count == 1
        ? await ResolveConcreteParentFormAsync(db, app, candidates[0], parentId: null, ct, instanceCache)
        : null;
    }

    foreach (var candidateType in candidates)
    {
      if (instanceCache == null || !instanceCache.TryGetValue(candidateType, out var candidateForm))
      {
        candidateForm = ModuleCatalog.Instance.CreateServiceInstance(candidateType, db, app) as Form.IFormService;
        if (candidateForm != null)
        {
          if (app != null)
          {
            candidateForm.AttachPlatformApp(app);
          }

          instanceCache?.TryAdd(candidateType, candidateForm);
        }
      }

      if (candidateForm == null)
      {
        continue;
      }

      candidateForm.SetRequest(new Form.Contracts.FormRequest { Id = parentId });
      if (await candidateForm.LoadParentRecordAsync(parentId.Value, ct) != null)
      {
        return candidateForm;
      }
    }

    return null;
  }
}
