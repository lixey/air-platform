using Microsoft.EntityFrameworkCore.Metadata;
using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  public virtual Task<object?> GetModelAsync(long? id, CancellationToken ct = default) =>
    GetModelAsync(new FormRequest { Id = id }, ct);

  public virtual async Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default)
  {
    SetRequest(request);
    var id = request.Id;
    IsNew = id is null or 0;

    var parent = GetParent();
    if (!IsNew && CurrentEntity?.Id != id!.Value)
    {
      CurrentEntity = await LoadEntityWithIncludesAsync(id.Value, State.LastElements, asTracking: true, ct)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);
      RestoreBoundParentId(CurrentEntity, parent);
    }
    else if (!IsNew && CurrentEntity != null)
    {
      RestoreBoundParentId(CurrentEntity, parent);
    }

    await PreloadParentRecordAsync(parent, ct);

    var elements = State.LastElements ?? await EnsureElementsAsync(ct, reload: true);

    TEntity entity;
    if (IsNew)
    {
      entity = CreateBlankEntity(parent, Request.ParentId);
    }
    else
    {
      var efModel = db.Model.FindEntityType(typeof(TEntity));
      var (refNames, includePaths) = CollectElementNavigationIncludes(efModel, elements);
      var needsQuery = CurrentEntity?.Id != id!.Value ||
                       HasMissingIncludedNavigations(CurrentEntity!, efModel, refNames, includePaths);

      entity = needsQuery
        ? await QueryEntityWithIncludes(refNames, includePaths, asTracking: true).FirstOrDefaultAsync(e => e.Id == id!.Value, ct)
          ?? throw new KeyNotFoundException(L["FormNotFound"])
        : CurrentEntity!;

      RestoreBoundParentId(entity, parent);
    }

    CurrentEntity = entity;
    State.LastSavedEntity = entity;

    var hooks = GetHooks();
    if (IsNew)
    {
      await InitializeNewRecordAsync(entity, elements, parent, hooks, ct);
    }
    else
    {
      await PopulateExistingRecordAsync(entity, elements, hooks, ct);
    }

    if (IsNew && request.Value is { ValueKind: JsonValueKind.Object } presetJson)
    {
      foreach (var prop in presetJson.EnumerateObject())
      {
        if (prop.Name.Contains('.'))
        {
          continue;
        }

        var targetName = ResolvePresetPropertyName(prop.Name);
        FormValueBinder.SetPropertyValue(entity, targetName, prop.Value);
      }
    }

    return entity;
  }

  private static readonly ConcurrentDictionary<(Type ServiceType, IModel Model), (HashSet<string> RefNames, HashSet<string> IncludePaths)?> StaticElementIncludesCache = new();

  private Task<TEntity?> LoadEntityWithIncludesAsync(
    long id,
    FormElements<TEntity>? elements,
    bool asTracking,
    CancellationToken ct)
  {
    var efModel = db.Model.FindEntityType(typeof(TEntity));
    if (efModel == null)
    {
      return Reachable(asTracking).FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    if (elements != null)
    {
      var (refNames, includePaths) = CollectElementNavigationIncludes(efModel, elements);
      return QueryEntityWithIncludes(refNames, includePaths, asTracking).FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    if (GetOrProbeStaticElementIncludes(efModel) is { } staticIncludes)
    {
      return QueryEntityWithIncludes(staticIncludes.RefNames, staticIncludes.IncludePaths, asTracking).FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    return Reachable(asTracking).FirstOrDefaultAsync(e => e.Id == id, ct);
  }

  private (HashSet<string> RefNames, HashSet<string> IncludePaths)? GetOrProbeStaticElementIncludes(IEntityType efModel)
  {
    var serviceType = GetType();
    if (ServiceOverrideFlags.For(serviceType).OverridesSetElementsAsync)
    {
      return null;
    }

    return StaticElementIncludesCache.GetOrAdd((serviceType, efModel.Model), _ =>
    {
      try
      {
        var probe = new FormElements<TEntity>();
        var savedElements = Elements;
        Elements = probe;
        try
        {
          SetElements();
        }
        finally
        {
          Elements = savedElements;
        }

        return CollectElementNavigationIncludes(efModel, probe);
      }
      catch
      {
        return null;
      }
    });
  }

  private IQueryable<TEntity> QueryEntityWithIncludes(
    HashSet<string> refNames,
    HashSet<string>? includePaths,
    bool asTracking)
  {
    var query = Reachable(asTracking);
    var efModel = db.Model.FindEntityType(typeof(TEntity));
    if (efModel == null)
    {
      return query;
    }

    foreach (var nav in efModel.GetNavigations())
    {
      if (!nav.IsCollection && !nav.TargetEntityType.IsOwned() && refNames.Contains(nav.Name))
      {
        query = query.Include(nav.Name);
      }
    }

    if (includePaths is { Count: > 0 })
    {
      foreach (var incPath in includePaths)
      {
        query = query.Include(incPath);
      }
    }

    return query;
  }

  private static (HashSet<string> RefNames, HashSet<string> IncludePaths) CollectElementNavigationIncludes(
    IEntityType? efModel,
    FormElements<TEntity> elements)
  {
    var refNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var includePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (efModel == null)
    {
      return (refNames, includePaths);
    }

    var rawInfoNavPaths = new List<string>();
    foreach (var el in elements.List)
    {
      var binding = FormElementBinding.TryGet(el);
      if (!string.IsNullOrEmpty(binding?.NavigationPropertyName))
      {
        refNames.Add(binding.NavigationPropertyName);
      }
      else if (!string.IsNullOrEmpty(el.ReferencePath))
      {
        refNames.Add(el.ReferencePath);
      }
      else if (!string.IsNullOrEmpty(el.Path) && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
      {
        refNames.Add(el.Path[..^2]);
      }

      if (binding?.InfoNavigationPaths is { Count: > 0 } navPaths)
      {
        rawInfoNavPaths.AddRange(navPaths);
      }
    }

    foreach (var validNavPath in ResolveValidEntityNavigationPaths(efModel, rawInfoNavPaths))
    {
      refNames.Add(validNavPath.Split('.')[0]);
      if (validNavPath.Contains('.'))
      {
        includePaths.Add(validNavPath);
      }
    }

    if (elements.StatusFlow is IRecordStatuses<TEntity> sf &&
        (sf.StatusMember.Body as MemberExpression ?? (sf.StatusMember.Body as UnaryExpression)?.Operand as MemberExpression)?.Member.Name is { Length: > 2 } statusProp &&
        statusProp.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
      refNames.Add(statusProp[..^2]);
    }

    return (refNames, includePaths);
  }

  private static bool HasMissingIncludedNavigations(
    TEntity entity,
    IEntityType? efModel,
    HashSet<string> refNames,
    HashSet<string> includePaths)
  {
    if (efModel == null)
    {
      return false;
    }

    foreach (var nav in efModel.GetNavigations())
    {
      if (!nav.IsCollection && !nav.TargetEntityType.IsOwned() && refNames.Contains(nav.Name) &&
          IsNavigationSegmentUnloaded(entity, efModel, nav.Name))
      {
        return true;
      }
    }

    foreach (var path in includePaths)
    {
      var segments = path.Split('.');
      object? currentObj = entity;
      var currentModel = efModel;
      foreach (var segment in segments)
      {
        if (currentObj == null || currentModel == null)
        {
          break;
        }

        if (IsNavigationSegmentUnloaded(currentObj, currentModel, segment))
        {
          return true;
        }

        currentObj = PropertyAccessorCache.GetValue(currentObj, segment);
        currentModel = currentModel.GetNavigations()
          .FirstOrDefault(n => !n.IsCollection && string.Equals(n.Name, segment, StringComparison.OrdinalIgnoreCase))
          ?.TargetEntityType;
      }
    }

    return false;
  }

  private static bool IsNavigationSegmentUnloaded(object instance, IEntityType model, string navName)
  {
    var nav = model.GetNavigations()
      .FirstOrDefault(n => !n.IsCollection && string.Equals(n.Name, navName, StringComparison.OrdinalIgnoreCase));
    if (nav == null)
    {
      return false;
    }

    var fkPropName = nav.ForeignKey.Properties.FirstOrDefault()?.Name;
    if (fkPropName == null ||
        !FormValidator.TryExtractIntegralOrEnumId(PropertyAccessorCache.GetValue(instance, fkPropName), out var fkId) ||
        fkId <= 0)
    {
      return false;
    }

    var navVal = PropertyAccessorCache.GetValue(instance, nav.Name);
    return navVal == null || navVal is IEntity navEntity && navEntity.Id != fkId;
  }

  public virtual async Task<FormDataDto> BuildFormDataAsync(
    object? entity,
    ScreenDto? screen = null,
    string? outcome = null,
    CancellationToken ct = default)
  {
    if (entity is TEntity typedEntity && typedEntity.Id > 0)
    {
      CurrentEntity ??= typedEntity;
    }

    var entityId = (entity as IEntity)?.Id
      ?? (State.LastSavedEntity?.Id is > 0 ? State.LastSavedEntity.Id : (long?)null)
      ?? (outcome == null ? Request.Id : null);
    var ver = (entity as IHasVersion)?.Ver
      ?? (State.LastSavedEntity as IHasVersion)?.Ver
      ?? (outcome == null && entityId > 0 ? 1 : null);

    var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
    var rawDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
    var dict = new Dictionary<string, object?>(rawDict, StringComparer.OrdinalIgnoreCase);
    var props = PropertyAccessorCache.GetProperties(typeof(TEntity))
      .Concat(entity != null && entity.GetType() != typeof(TEntity) ? PropertyAccessorCache.GetProperties(entity.GetType()) : []);
    foreach (var prop in props)
    {
      if (prop.GetCustomAttribute<SensitiveAttribute>() != null)
      {
        var camel = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
        if (dict.ContainsKey(camel))
        {
          dict[camel] = null;
        }
      }
    }

    if (outcome == null && entity is IReadOnlyEntity && dict.TryGetValue("name", out var nameObj) && nameObj != null)
    {
      var nameStr = nameObj is JsonElement je && je.ValueKind == JsonValueKind.String ? je.GetString() : nameObj.ToString();
      if (!string.IsNullOrEmpty(nameStr))
      {
        dict["name"] = L[nameStr];
      }
    }

    if (screen != null)
    {
      await PopulateReferencesAsync(dict, entity, screen, ct);
    }
    else
    {
      await PopulateReferencesAsync(dict, entity, ct);
    }

    var elements = await EnsureElementsAsync(ct);
    State.LastSavedEntity = await PopulateInfoValuesAsync(elements.List, dict, entity, entityId, ct);

    var unavailable = await EvaluateUnavailableAsync(entityId, ct);

    return new FormDataDto
    {
      Id = outcome == null ? (entityId > 0 ? entityId : null) : entityId,
      Ver = outcome == null ? (entityId > 0 ? ver : null) : ver,
      Value = dict,
      Outcome = outcome,
      Unavailable = unavailable,
      Warnings = !string.IsNullOrEmpty(outcome) ? GetLastSaveWarnings()?.ToList() : null,
      Affected = !string.IsNullOrEmpty(outcome) ? State.LastAffectedParents : null
    };
  }

  public virtual async Task<FormDraftResultDto> PrepareDraftAsync(
    long? recordId = null,
    IReadOnlyDictionary<string, object?>? overrides = null,
    ISet<string>? pendingReferencePaths = null,
    long? parentId = null,
    string? parentService = null,
    string? triggerField = null,
    CancellationToken ct = default)
  {
    var isEdit = recordId is > 0;
    var parent = GetParent();
    if (parentId is null or <= 0 && parent.BoundProperty != null && overrides != null)
    {
      foreach (var (k, v) in overrides)
      {
        if (string.Equals(k, parent.BoundProperty.Name, StringComparison.OrdinalIgnoreCase) &&
            FormDraftEngine.ExtractReferenceId(v) is > 0 and var overridePid)
        {
          parentId = overridePid;
          break;
        }
      }
    }

    var req = new FormRequest
    {
      Id = isEdit ? recordId : null,
      ParentId = parentId,
      ParentService = parentService
    };
    SetRequest(req);
    IsNew = !isEdit;
    State.LastElements = null;

    if (!isEdit)
    {
      await PreloadParentRecordAsync(parent, ct);
    }

    object? modelObj = null;
    try
    {
      modelObj = await GetModelAsync(req, ct);
    }
    catch when (!isEdit && !parentId.HasValue && string.IsNullOrEmpty(parentService))
    {
    }

    if (Request.ParentId is null or <= 0 &&
        parent.BoundProperty != null &&
        CurrentEntity != null &&
        parent.BoundProperty.GetValue(CurrentEntity) is long entityPid and > 0)
    {
      Request.ParentId = entityPid;
    }

    var parentReloaded = false;
    if (Request.ParentId is > 0 && State.PreloadedParentId != Request.ParentId)
    {
      await PreloadParentRecordAsync(parent, ct);
      parentReloaded = true;
    }

    var elements = await EnsureElementsAsync(ct, reload: parentReloaded || State.LastElements == null);
    var hooks = GetHooks();
    return await FormDraftEngine.BuildDraftResultAsync(
      db,
      PlatformApp,
      elements.List,
      modelObj,
      CurrentEntity,
      isEdit,
      overrides,
      pendingReferencePaths,
      triggerField,
      hooks.ComputeActions.Count > 0,
      async (draftEntity, originalValues, explicitlyProvided) =>
      {
        if (Request.ParentId is null or <= 0 &&
            parent.BoundProperty != null &&
            parent.BoundProperty.GetValue(draftEntity) is long draftPid and > 0)
        {
          Request.ParentId = draftPid;
        }

        if (Request.ParentId is > 0 && State.PreloadedParentId != Request.ParentId)
        {
          await PreloadParentRecordAsync(parent, ct);
        }

        var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(
          elements,
          hooks,
          draftEntity,
          originalValues,
          explicitlyProvided,
          ct);
        return (saveCtx.ToErrorMessages(), ruleCtx.Failures);
      },
      ct);
  }

  private TEntity CreateBlankEntity(FormParent<TEntity> parent, long? parentId)
  {
    var entity = new TEntity();
    if (entity is IHasTimestamps timestamps && timestamps.CreatedAt == default)
    {
      timestamps.CreatedAt = Time.GetUtcNow();
    }

    if (entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
    {
      tenantEntity.TenantId = tid;
    }

    ApplyBoundParentId(entity, parent, parentId);
    return entity;
  }

  private async Task InitializeNewRecordAsync(
    TEntity entity,
    FormElements<TEntity> elements,
    FormParent<TEntity> parent,
    FormHooks<TEntity> hooks,
    CancellationToken ct)
  {
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
    {
      statusFlow.Initialize(entity);
    }

    ApplyParentChildPreset(entity, parent);

    var loadCtx = new FormLoadContext<TEntity>
    {
      Entity = entity,
      IsNew = true
    };
    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }
  }

  private async Task PopulateExistingRecordAsync(
    TEntity entity,
    FormElements<TEntity> elements,
    FormHooks<TEntity> hooks,
    CancellationToken ct)
  {
    await PopulateJoinedReferencesAsync(elements.List, entity, ct);

    var loadCtx = new FormLoadContext<TEntity>
    {
      Entity = entity,
      IsNew = false
    };
    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }
  }
}
