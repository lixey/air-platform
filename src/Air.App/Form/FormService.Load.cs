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
      CurrentEntity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct)
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
      var query = Reachable(asTracking: true);
      var needsQuery = CurrentEntity?.Id != id!.Value;
      var efModel = db.Model.FindEntityType(typeof(TEntity));
      if (efModel != null)
      {
        var refNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawInfoNavPaths = new List<string>();
        foreach (var el in elements.List)
        {
          if (!string.IsNullOrEmpty(el.NavigationPropertyName))
          {
            refNames.Add(el.NavigationPropertyName);
          }
          else if (!string.IsNullOrEmpty(el.ReferencePath))
          {
            refNames.Add(el.ReferencePath);
          }
          else if (!string.IsNullOrEmpty(el.Path) && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
          {
            refNames.Add(el.Path[..^2]);
          }

          if (el.InfoNavigationPaths is { Count: > 0 } navPaths)
          {
            rawInfoNavPaths.AddRange(navPaths);
          }
        }

        var includePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        foreach (var nav in efModel.GetNavigations())
        {
          if (!nav.IsCollection && refNames.Contains(nav.Name))
          {
            query = query.Include(nav.Name);
            if (!needsQuery && PropertyAccessorCache.GetValue(CurrentEntity!, nav.Name) == null)
            {
              var fkPropName = nav.ForeignKey.Properties.FirstOrDefault()?.Name;
              if (fkPropName != null &&
                  FormValidator.TryExtractIntegralOrEnumId(PropertyAccessorCache.GetValue(CurrentEntity!, fkPropName), out var fkId) &&
                  fkId > 0)
              {
                needsQuery = true;
              }
            }
          }
        }

        foreach (var incPath in includePaths)
        {
          query = query.Include(incPath);
        }
      }

      entity = needsQuery
        ? await query.FirstOrDefaultAsync(e => e.Id == id!.Value, ct) ?? throw new KeyNotFoundException(L["FormNotFound"])
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
        dict["name"] = Localizer.Instance[nameStr];
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

    if (isEdit && CurrentEntity?.Id != recordId!.Value)
    {
      CurrentEntity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == recordId.Value, ct)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);
      RestoreBoundParentId(CurrentEntity, parent);
    }

    await PreloadParentRecordAsync(parent, ct);

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

    if (Request.ParentId is > 0 && State.PreloadedParentId != Request.ParentId)
    {
      await PreloadParentRecordAsync(parent, ct);
    }

    var elements = await EnsureElementsAsync(ct, reload: true);
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
        return (saveCtx.Errors, ruleCtx.Failures);
      },
      ct);
  }

  private TEntity CreateBlankEntity(FormParent<TEntity> parent, long? parentId)
  {
    var entity = new TEntity();
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
