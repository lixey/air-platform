namespace Platform.App.Internals;

internal static class BoundParentRecalculator
{
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

    var parentEntityType = ParentScopeValidator.GetFormEntityType(baseParentType);
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

  public static async Task<Form.IFormService?> ResolveConcreteParentFormAsync(
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
