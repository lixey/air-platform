using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormModelLoader
{
  public static async Task<object?> LoadModelAsync<TEntity>(
    FormService<TEntity> form,
    FormRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    form.SetRequest(request);
    var id = request.Id;
    form.IsNewRecord = id is null or 0;

    var db = form.DbContext;
    var parent = form.GetParentDeclaration();
    if (!form.IsNewRecord && form.Entity?.Id != id!.Value)
    {
      form.Entity = await form.ReachableQuery(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct)
        ?? throw new KeyNotFoundException(form.L["FormNotFound"]);
      form.RestoreBoundParentId(form.Entity, parent);
    }
    else if (!form.IsNewRecord && form.Entity != null)
    {
      form.RestoreBoundParentId(form.Entity, parent);
    }

    await form.PreloadParentRecordInternalAsync(parent, ct);

    var elements = form.State.LastElements ?? await form.EnsureElementsAsync(ct, reload: true);

    TEntity entity;
    if (form.IsNewRecord)
    {
      entity = CreateBlankEntity(form, parent, form.CurrentRequest.ParentId);
    }
    else
    {
      var query = form.ReachableQuery(asTracking: true);
      var needsQuery = form.Entity?.Id != id!.Value;
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
        foreach (var validNavPath in FormReferenceResolver.ResolveValidEntityNavigationPaths(efModel, rawInfoNavPaths))
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
            if (!needsQuery && PropertyAccessorCache.GetValue(form.Entity!, nav.Name) == null)
            {
              var fkPropName = nav.ForeignKey.Properties.FirstOrDefault()?.Name;
              if (fkPropName != null &&
                  FormValidator.TryExtractIntegralOrEnumId(PropertyAccessorCache.GetValue(form.Entity!, fkPropName), out var fkId) &&
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
        ? await query.FirstOrDefaultAsync(e => e.Id == id!.Value, ct) ?? throw new KeyNotFoundException(form.L["FormNotFound"])
        : form.Entity!;

      form.RestoreBoundParentId(entity, parent);
    }

    form.Entity = entity;
    form.State.LastSavedEntity = entity;

    var hooks = form.GetHooksDeclaration();
    if (form.IsNewRecord)
    {
      await InitializeNewRecordAsync(form, entity, elements, parent, hooks, ct);
    }
    else
    {
      await PopulateExistingRecordAsync(form, entity, elements, hooks, ct);
    }

    if (form.IsNewRecord && request.Value is { ValueKind: JsonValueKind.Object } presetJson)
    {
      foreach (var prop in presetJson.EnumerateObject())
      {
        if (prop.Name.Contains('.'))
        {
          continue;
        }

        var targetName = FormScreenBuilder.ResolvePresetPropertyName<TEntity>(prop.Name);
        FormValueBinder.SetPropertyValue(entity, targetName, prop.Value);
      }
    }

    return entity;
  }

  public static async Task<FormDataDto> BuildFormDataAsync<TEntity>(
    FormService<TEntity> form,
    object? entity,
    ScreenDto? screen,
    string? outcome,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (entity is TEntity typedEntity && typedEntity.Id > 0)
    {
      form.Entity ??= typedEntity;
    }

    var entityId = (entity as IEntity)?.Id
      ?? (form.State.LastSavedEntity?.Id is > 0 ? form.State.LastSavedEntity.Id : (long?)null)
      ?? (outcome == null ? form.CurrentRequest.Id : null);
    var ver = (entity as IHasVersion)?.Ver
      ?? (form.State.LastSavedEntity as IHasVersion)?.Ver
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
      await form.PopulateReferencesAsync(dict, entity, screen, ct);
    }
    else
    {
      await form.PopulateReferencesAsync(dict, entity, ct);
    }

    var elements = await form.EnsureElementsAsync(ct);
    form.State.LastSavedEntity = await FormReferenceResolver.PopulateInfoValuesAsync(
      form.DbContext,
      form.ReachableQuery,
      elements.List,
      dict,
      entity,
      form.State.LastSavedEntity,
      entityId,
      ct);

    var unavailable = await form.EvaluateUnavailableAsync(entityId, ct);

    return new FormDataDto
    {
      Id = outcome == null ? (entityId > 0 ? entityId : null) : entityId,
      Ver = outcome == null ? (entityId > 0 ? ver : null) : ver,
      Value = dict,
      Outcome = outcome,
      Unavailable = unavailable,
      Warnings = !string.IsNullOrEmpty(outcome) ? form.GetLastSaveWarnings()?.ToList() : null,
      Affected = !string.IsNullOrEmpty(outcome) ? form.State.LastAffectedParents : null
    };
  }

  public static async Task PopulateReferencesAsync<TEntity>(
    FormService<TEntity> form,
    Dictionary<string, object?> dict,
    object? entity,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (entity == null)
    {
      return;
    }

    var elements = await form.EnsureElementsAsync(ct);
    await FormReferenceResolver.PopulateReferencesCoreAsync(
      form.DbContext,
      form.PlatformApp,
      dict,
      entity,
      form.Entity,
      form.State.LastSavedEntity,
      elements.List,
      ct);
  }

  public static async Task PopulateReferencesAsync<TEntity>(
    FormService<TEntity> form,
    Dictionary<string, object?> dict,
    object? entity,
    ScreenDto screen,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (entity == null || screen.Layout?.Root == null)
    {
      return;
    }

    var formModel = FormReferenceResolver.FindFormModel(screen.Layout.Root);
    if (formModel == null)
    {
      return;
    }

    await FormReferenceResolver.PopulateReferencesCoreAsync(
      form.DbContext,
      form.PlatformApp,
      dict,
      entity,
      form.Entity,
      form.State.LastSavedEntity,
      formModel.Elements,
      ct);
  }

  public static async Task<FormDraftResultDto> PrepareDraftAsync<TEntity>(
    FormService<TEntity> form,
    long? recordId,
    IReadOnlyDictionary<string, object?>? overrides,
    ISet<string>? pendingReferencePaths,
    long? parentId,
    string? parentService,
    string? triggerField,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var isEdit = recordId is > 0;
    var parent = form.GetParentDeclaration();
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
    form.SetRequest(req);
    form.IsNewRecord = !isEdit;
    form.State.LastElements = null;

    if (isEdit && form.Entity?.Id != recordId!.Value)
    {
      form.Entity = await form.ReachableQuery(asTracking: true).FirstOrDefaultAsync(e => e.Id == recordId.Value, ct)
        ?? throw new KeyNotFoundException(form.L["FormNotFound"]);
      form.RestoreBoundParentId(form.Entity, parent);
    }

    await form.PreloadParentRecordInternalAsync(parent, ct);

    object? modelObj = null;
    try
    {
      modelObj = await form.GetModelAsync(req, ct);
    }
    catch when (!isEdit && !parentId.HasValue && string.IsNullOrEmpty(parentService))
    {
    }

    if (form.CurrentRequest.ParentId is null or <= 0 &&
        parent.BoundProperty != null &&
        form.Entity != null &&
        parent.BoundProperty.GetValue(form.Entity) is long entityPid and > 0)
    {
      form.CurrentRequest.ParentId = entityPid;
    }

    if (form.CurrentRequest.ParentId is > 0 && form.State.PreloadedParentId != form.CurrentRequest.ParentId)
    {
      await form.PreloadParentRecordInternalAsync(parent, ct);
    }

    var elements = await form.EnsureElementsAsync(ct, reload: true);
    var hooks = form.GetHooksDeclaration();
    return await FormDraftEngine.BuildDraftResultAsync(
      form.DbContext,
      form.PlatformApp,
      elements.List,
      modelObj,
      form.Entity,
      isEdit,
      overrides,
      pendingReferencePaths,
      triggerField,
      hooks.ComputeActions.Count > 0,
      async (draftEntity, originalValues, explicitlyProvided) =>
      {
        if (form.CurrentRequest.ParentId is null or <= 0 &&
            parent.BoundProperty != null &&
            parent.BoundProperty.GetValue(draftEntity) is long draftPid and > 0)
        {
          form.CurrentRequest.ParentId = draftPid;
        }

        if (form.CurrentRequest.ParentId is > 0 && form.State.PreloadedParentId != form.CurrentRequest.ParentId)
        {
          await form.PreloadParentRecordInternalAsync(parent, ct);
        }

        var (saveCtx, ruleCtx) = await FormSavePipeline.RunValidationPipelineAsync(
          form,
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

  public static TEntity CreateBlankEntity<TEntity>(
    FormService<TEntity> form,
    FormService<TEntity>.Parent parent,
    long? parentId)
    where TEntity : class, IEntity, new()
  {
    var entity = new TEntity();
    if (entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && form.DbContext.TenantContext.TenantId is { } tid)
    {
      tenantEntity.TenantId = tid;
    }

    form.ApplyBoundParentId(entity, parent, parentId);
    return entity;
  }

  public static async Task InitializeNewRecordAsync<TEntity>(
    FormService<TEntity> form,
    TEntity entity,
    FormService<TEntity>.Elements elements,
    FormService<TEntity>.Parent parent,
    FormService<TEntity>.Hooks hooks,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
    {
      statusFlow.Initialize(entity);
    }

    form.ApplyParentChildPreset(entity, parent);

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

  public static async Task PopulateExistingRecordAsync<TEntity>(
    FormService<TEntity> form,
    TEntity entity,
    FormService<TEntity>.Elements elements,
    FormService<TEntity>.Hooks hooks,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    await FormReferenceResolver.PopulateJoinedReferencesAsync(form.DbContext, elements.List, entity, ct);

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
