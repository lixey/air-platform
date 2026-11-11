namespace Platform.App.Internals;

internal static class ParentScopeValidator
{
  private static readonly ConcurrentDictionary<Type, Type?> CachedConventionFormTypes = new();

  public static Type? ResolveConventionFormType(Type? gridServiceType)
  {
    if (gridServiceType == null)
    {
      return null;
    }

    return CachedConventionFormTypes.GetOrAdd(gridServiceType, static t =>
    {
      var candidateName = t.Name.Replace("GridService", "FormService");
      return t.Assembly.GetType(t.Namespace + "." + candidateName)
        ?? t.Assembly.GetTypes().FirstOrDefault(x => x.Name == candidateName);
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
      if (parentServiceType != null &&
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

    if (parentServiceType is { IsAbstract: true } &&
        typeof(Form.IFormService).IsAssignableFrom(parentServiceType))
    {
      var candidates = ModuleCatalog.Instance.AllServiceTypes
        .Where(t => !t.IsAbstract && parentServiceType.IsAssignableFrom(t))
        .ToList();
      var distinctFeatures = candidates
        .Select(ModuleCatalog.Instance.FindFeature)
        .Where(f => f is not null)
        .Distinct()
        .ToList();
      if (candidates.Count > 0 && distinctFeatures.Count <= 1)
      {
        if (distinctFeatures is [var sharedFeature] &&
            sharedFeature is not null &&
            !string.IsNullOrEmpty(sharedFeature.Module) &&
            (app?.User is not { IsAuthenticated: true } user ||
             !ServiceEndpointsExtensions.CanView(sharedFeature, user)))
        {
          return (false, null, null);
        }

        if (lockParent && parentEntityType != null && db.Model.FindEntityType(parentEntityType) != null)
        {
          await MutationContext.AcquireLocksAsync(db, parentEntityType, [[parentId.Value]], ct);
        }

        foreach (var candidateType in candidates)
        {
          if (ModuleCatalog.Instance.CreateServiceInstance(candidateType, db, app) is not Form.IFormService candidateForm)
          {
            continue;
          }

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
    }

    if (parentServiceType != null &&
        typeof(Form.IFormService).IsAssignableFrom(parentServiceType) &&
        ModuleCatalog.Instance.CreateServiceInstance(parentServiceType, db, app) is Form.IFormService parentForm)
    {
      if (!string.IsNullOrEmpty(parentForm.Feature.Module) &&
          (app?.User is not { IsAuthenticated: true } user ||
           !ServiceEndpointsExtensions.CanView(parentForm.Feature, user)))
      {
        return (false, null, null);
      }

      if (lockParent && parentEntityType != null && db.Model.FindEntityType(parentEntityType) != null)
      {
        await MutationContext.AcquireLocksAsync(db, parentEntityType, [[parentId.Value]], ct);
      }

      parentForm.SetRequest(new Form.Contracts.FormRequest { Id = parentId });
      var loaded = await parentForm.LoadParentRecordAsync(parentId.Value, ct);
      if (loaded == null)
      {
        return (false, null, null);
      }

      var (_, children) = await parentForm.GetElementsMetadataAsync(ct);
      return (true, loaded, children);
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
    long? parentId,
    Action<(string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)> setCache,
    (string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)? cache,
    Func<Form.ChildGridBuilder, bool> predicate,
    CancellationToken ct = default)
  {
    var effectiveParentService = !string.IsNullOrEmpty(requestParentService)
      ? requestParentService
      : boundParentService;
    if (string.IsNullOrEmpty(effectiveParentService))
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
      var parentFormType = ModuleCatalog.Instance.FindServiceType(effectiveParentService);
      if (parentFormType != null &&
          typeof(Form.IFormService).IsAssignableFrom(parentFormType) &&
          ModuleCatalog.Instance.CreateServiceInstance(parentFormType, db, app) is Form.IFormService parentForm)
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

    var parentServiceType = (!string.IsNullOrEmpty(requestParentService)
      ? ModuleCatalog.Instance.FindServiceType(requestParentService)
      : null) ?? boundParentServiceType;
    if (parentServiceType == null && !string.IsNullOrEmpty(boundParentService))
    {
      parentServiceType = ModuleCatalog.Instance.FindServiceType(boundParentService);
    }

    if (parentServiceType == null ||
        !typeof(Form.IFormService).IsAssignableFrom(parentServiceType) ||
        ModuleCatalog.Instance.CreateServiceInstance(parentServiceType, db, app) is not Form.IFormService parentForm)
    {
      return null;
    }

    if (app != null)
    {
      parentForm.AttachPlatformApp(app);
    }

    if (parentForm.HasCustomRecalculate)
    {
      await db.RunDerivedRecalculationAsync(
        token => parentForm.RecalculateParentAsync(distinctIds, token),
        ct);
    }

    if (distinctIds.Count > 5 || !ServiceEndpointsExtensions.CanView(parentForm.Feature, app?.User))
    {
      return null;
    }

    var parentEntityType = GetFormEntityType(parentServiceType);
    List<AffectedDto>? affected = null;
    foreach (var id in distinctIds)
    {
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
}
