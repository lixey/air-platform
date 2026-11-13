using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
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

    var elements = _state.LastElements ?? await EnsureElementsAsync(ct, reload: true);

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
    _state.LastSavedEntity = entity;

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
      ?? (_state.LastSavedEntity?.Id is > 0 ? _state.LastSavedEntity.Id : (long?)null)
      ?? (outcome == null ? Request.Id : null);
    var ver = (entity as IHasVersion)?.Ver
      ?? (_state.LastSavedEntity as IHasVersion)?.Ver
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
    _state.LastSavedEntity = await FormReferenceResolver.PopulateInfoValuesAsync(
      db,
      Reachable,
      elements.List,
      dict,
      entity,
      _state.LastSavedEntity,
      entityId,
      ct);

    var unavailable = await EvaluateUnavailableAsync(entityId, ct);

    return new FormDataDto
    {
      Id = outcome == null ? (entityId > 0 ? entityId : null) : entityId,
      Ver = outcome == null ? (entityId > 0 ? ver : null) : ver,
      Value = dict,
      Outcome = outcome,
      Unavailable = unavailable,
      Warnings = !string.IsNullOrEmpty(outcome) ? GetLastSaveWarnings()?.ToList() : null,
      Affected = !string.IsNullOrEmpty(outcome) ? _state.LastAffectedParents : null
    };
  }

  public virtual async Task PopulateReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    CancellationToken ct = default)
  {
    if (entity == null)
    {
      return;
    }

    var elements = await EnsureElementsAsync(ct);
    await FormReferenceResolver.PopulateReferencesCoreAsync(
      db,
      PlatformApp,
      dict,
      entity,
      CurrentEntity,
      _state.LastSavedEntity,
      elements.List,
      ct);
  }

  public virtual async Task PopulateReferencesAsync(
    Dictionary<string, object?> dict,
    object? entity,
    ScreenDto screen,
    CancellationToken ct = default)
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
      db,
      PlatformApp,
      dict,
      entity,
      CurrentEntity,
      _state.LastSavedEntity,
      formModel.Elements,
      ct);
  }

  public virtual Task<object> SaveAsync(JsonElement json, CancellationToken ct = default) =>
    SaveAsync(json, null, ct);

  public virtual async Task<object> SaveAsync(JsonElement json, FormRequest? request = null, CancellationToken ct = default)
  {
    var (req, entityJson) = FormRequest.Normalize(json, request);
    if (req.Value is not { ValueKind: JsonValueKind.Object })
    {
      throw new JsonException("FormRequest 'value' object is required.");
    }

    var input = FormValueBinder.DeserializeInput<TEntity>(entityJson);

    var id = req.Id;
    var clientVer = req.Ver;
    SetRequest(req);
    IsNew = id is null or 0;
    _state.ResetSaveOutcome();

    var savedResult = IsNew
      ? await MutationRunner.RunIdempotentCreateAsync<object>(
        db,
        Service,
        req.IdempotencyKey,
        req.RawPayload ?? json.GetRawText(),
        mutationCt => SaveCoreAsync(entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        async (replayedId, reloadCt) =>
        {
          Request.Id = replayedId;
          return await GetModelAsync(Request, reloadCt)
            ?? await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == replayedId, reloadCt);
        },
        _ => _state.LastSavedEntity?.Id,
        ct,
        PlatformApp?.Services)
      : await db.RunMutationAsync(
        mutationCt => SaveCoreAsync(entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        ct: ct);

    if (PlatformApp != null && _state.LastSavedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(_state.LastSavedEntity, db, ct);
    }

    return savedResult;
  }

  private async Task<TEntity> LockAndLoadExistingAsync(long id, Parent parent, CancellationToken ct)
  {
    var requestedParentId = Request.ParentId;
    if (parent.BoundProperty != null &&
        (parent.MatchesService(Request.ParentService) || (await ResolveParentChildBuilderAsync(requirePreset: true, ct))?.PresetProvider == null))
    {
      var probe = await db.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
      if (probe != null)
      {
        var probeParentId = parent.BoundProperty.GetValue(probe) as long?;
        if (requestedParentId is > 0 && probeParentId is > 0 && probeParentId.Value != requestedParentId.Value)
        {
          throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]);
        }

        if (!parent.IsOptional || !Request.ParentId.HasValue)
        {
          if (probeParentId.HasValue)
          {
            Request.ParentId = probeParentId.Value;
          }
        }
      }
    }

    var lockedParentId = Request.ParentId;
    await EnsureParentScopeAsync(parent, lockParent: true, ct);
    await MutationContext.AcquireLocksAsync(db, typeof(TEntity), [[id]], ct);

    var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    Request.ParentId = requestedParentId;
    RestoreBoundParentId(entity, parent);
    if (Request.ParentId != lockedParentId)
    {
      await EnsureParentScopeAsync(parent, lockParent: true, ct);
    }

    return entity;
  }

  private async Task<object> SaveCoreAsync(
    JsonElement entityJson,
    TEntity input,
    long? id,
    int? clientVer,
    (string Path, JsonElement Value)? cellPatch,
    CancellationToken ct)
  {
    var parent = GetParent();
    var hooks = GetHooks();

    TEntity entity;
    Dictionary<string, object?>? originalValues = null;
    if (IsNew)
    {
      if (Request.ParentId is null or <= 0 &&
          parent.BoundProperty != null &&
          PropertyAccessorCache.GetValue(input, parent.BoundProperty.Name) is long inputPid and > 0)
      {
        Request.ParentId = inputPid;
      }

      await EnsureParentScopeAsync(parent, lockParent: true, ct);
      entity = CreateBlankEntity(parent, Request.ParentId);
      if (parent.BoundProperty != null && !parent.IsOptional)
      {
        var boundParentVal = PropertyAccessorCache.GetValue(entity, parent.BoundProperty.Name)
          ?? PropertyAccessorCache.GetValue(input, parent.BoundProperty.Name);
        if (boundParentVal is not long pid || pid <= 0)
        {
          throw new FormRefusedException(
            MutationOutcome.Refused,
            L["FormInvalid"],
            [new MessageDto
            {
              Path = Elements.ToCamelPath(parent.BoundProperty.Name),
              Detail = L["Required"],
              Code = "form.validation.required",
              Type = MessageType.Error
            }]);
        }
      }
    }
    else
    {
      entity = await LockAndLoadExistingAsync(id!.Value, parent, ct);

      if (entity is IHasVersion versionedEntity &&
          (!clientVer.HasValue || clientVer.Value <= 0 || versionedEntity.Ver != clientVer.Value))
      {
        throw new FormRefusedException(
          MutationOutcome.Conflict,
          L["MutationConflict"],
          [new MessageDto { Detail = L["FormRecordChanged"], Path = cellPatch?.Path, Type = MessageType.Error }]);
      }
    }

    CurrentEntity = entity;
    var elements = await EnsureElementsAsync(ct, reload: true);

    if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["FormReadOnly"],
        [new MessageDto { Detail = L["FormReadOnly"], Path = cellPatch?.Path, Type = MessageType.Error }]);
    }

    if (IsNew)
    {
      await InitializeNewRecordAsync(entity, elements, parent, hooks, ct);
    }
    else
    {
      await PopulateExistingRecordAsync(entity, elements, hooks, ct);
      originalValues = FormValueBinder.SnapshotValues(entity);
    }

    HashSet<string>? presentPaths;
    HashSet<string> disabledElementPaths;
    if (cellPatch is { } cp)
    {
      if (string.IsNullOrEmpty(cp.Path) || cp.Path.Contains('.'))
      {
        throw new FormRefusedException(
          MutationOutcome.Refused,
          L["FormReadOnly"],
          [new MessageDto { Detail = L["FormReadOnly"], Path = cp.Path, Type = MessageType.Error }]);
      }

      var targetElement = elements.List.FirstOrDefault(e => string.Equals(e.Path, cp.Path, StringComparison.OrdinalIgnoreCase))
                          ?? elements.List.FirstOrDefault(e => !string.IsNullOrEmpty(e.ReferencePath) && string.Equals(e.ReferencePath, cp.Path, StringComparison.OrdinalIgnoreCase));
      var effectiveCellPath = targetElement?.Path ?? cp.Path;
      presentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { effectiveCellPath };
      FormValueBinder.CopyMatchingProperties(entity, input);
      FormValueBinder.SetPropertyValue(input, effectiveCellPath, cp.Value);
      FormValueBinder.NormalizeNullableStrings(elements.List, input);
      disabledElementPaths = FormValueBinder.ComputeDisabledElementPaths(
        elements.List,
        parent.BoundProperty,
        parent.IsOptional,
        IsNew,
        entity,
        input,
        presentPaths);
      var protectedPaths = FormValueBinder.BuildProtectedPaths(
        elements.List,
        parent.BoundProperty,
        parent.IsOptional,
        presentPaths,
        IsNew,
        entity,
        input,
        disabledElementPaths);
      var propName = PropertyAccessorCache.GetProperty(typeof(TEntity), effectiveCellPath)?.Name ?? effectiveCellPath;
      if (targetElement == null ||
          targetElement.IsHidden ||
          targetElement.IsDisabled ||
          disabledElementPaths.Contains(targetElement.Path) ||
          targetElement.IsServerOwned ||
          targetElement.Model is FormInfoModel ||
          protectedPaths.Contains(propName))
      {
        throw new FormRefusedException(
          MutationOutcome.Refused,
          L["FormReadOnly"],
          [new MessageDto { Detail = L["FormReadOnly"], Path = cp.Path, Type = MessageType.Error }]);
      }

      FormValueBinder.SetPropertyValue(entity, effectiveCellPath, cp.Value);
      FormValueBinder.NormalizeNullableStrings(elements.List, entity);
      if (await FormSelectPatcher.ApplyServerSelectPatchesAsync(
            db,
            PlatformApp,
            elements.List,
            parent.BoundProperty,
            parent.IsOptional,
            IsNew,
            entity,
            originalValues,
            presentPaths,
            protectedPaths,
            disabledElementPaths,
            effectiveCellPath,
            ct) &&
          elements.List.Any(e => e.DisabledWhen != null))
      {
        disabledElementPaths = FormValueBinder.ComputeDisabledElementPaths(
          elements.List,
          parent.BoundProperty,
          parent.IsOptional,
          IsNew,
          entity,
          entity,
          presentPaths);
      }
    }
    else
    {
      presentPaths = FormValueBinder.BuildPresentPaths(typeof(TEntity), entityJson);
      FormValueBinder.NormalizeNullableStrings(elements.List, input);
      disabledElementPaths = FormValueBinder.ComputeDisabledElementPaths(
        elements.List,
        parent.BoundProperty,
        parent.IsOptional,
        IsNew,
        entity,
        input,
        presentPaths);
      var protectedPaths = FormValueBinder.BuildProtectedPaths(
        elements.List,
        parent.BoundProperty,
        parent.IsOptional,
        presentPaths,
        IsNew,
        entity,
        input,
        disabledElementPaths);
      FormValueBinder.CopyMatchingProperties(input, entity, protectedPaths);
      FormValueBinder.NormalizeNullableStrings(elements.List, entity);
      if (await FormSelectPatcher.ApplyServerSelectPatchesAsync(
            db,
            PlatformApp,
            elements.List,
            parent.BoundProperty,
            parent.IsOptional,
            IsNew,
            entity,
            originalValues,
            presentPaths,
            protectedPaths,
            disabledElementPaths,
            cellPatchPath: null,
            ct) &&
          elements.List.Any(e => e.DisabledWhen != null))
      {
        disabledElementPaths = FormValueBinder.ComputeDisabledElementPaths(
          elements.List,
          parent.BoundProperty,
          parent.IsOptional,
          IsNew,
          entity,
          entity,
          presentPaths);
      }

      if (id is > 0)
      {
        entity.Id = id.Value;
      }

      if (IsNew)
      {
        ApplyBoundParentId(entity, parent, Request.ParentId);
      }

      if (parent.BoundProperty != null && !parent.IsOptional)
      {
        var boundParentVal = PropertyAccessorCache.GetValue(entity, parent.BoundProperty.Name);
        if (boundParentVal is not long pid || pid <= 0)
        {
          throw new FormRefusedException(
            MutationOutcome.Refused,
            L["FormInvalid"],
            [new MessageDto
            {
              Path = Elements.ToCamelPath(parent.BoundProperty.Name),
              Detail = L["Required"],
              Code = "form.validation.required",
              Type = MessageType.Error
            }]);
        }
      }
    }

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, originalValues, presentPaths, ct, disabledElementPaths);
    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
    }

    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], ruleCtx.Failures);
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
    }

    FormValidator.ValidateElementConstraints(db, L, elements.List, entity, ruleCtx, onlyAcceptingInput: false, disabledElementPaths);
    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], ruleCtx.Failures);
    }

    if (IsNew)
    {
      db.Set<TEntity>().Add(entity);
    }
    else if (db.Entry(entity).State == EntityState.Detached)
    {
      db.Set<TEntity>().Update(entity);
    }

    await db.SaveChangesAsync(ct);
    await FormReferenceResolver.SyncJoinedReferencesAsync(db, elements.List, entity, ct);

    if (hooks.AfterSaveActions.Count > 0)
    {
      foreach (var h in hooks.AfterSaveActions)
      {
        await h(saveCtx, ct);
      }

      if (saveCtx.Errors.Count > 0)
      {
        throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(ct);
      }
    }

    if (parent.BoundProperty != null)
    {
      var oldParentId = !IsNew && originalValues != null && originalValues.TryGetValue(parent.BoundProperty.Name, out var oldVal) && oldVal is long oldPid
        ? oldPid
        : (long?)null;
      var newParentId = parent.BoundProperty.GetValue(entity) as long? ?? Request.ParentId;
      _state.LastAffectedParents = await ParentScopeValidator.RecalculateBoundParentsAsync(
        db,
        PlatformApp,
        Request.ParentService,
        parent.BoundParentService,
        parent.BoundParentServiceType,
        [oldParentId, newParentId],
        ct,
        requestParentId: Request.ParentId);
    }

    _state.LastSavedEntity = entity;
    _state.LastSaveWarnings = saveCtx.Warnings.Count > 0 ? [.. saveCtx.Warnings] : null;
    return entity;
  }

  public virtual async Task<GridCellDto> SaveCellAsync(
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<RowDto?>> readRow,
    CancellationToken ct = default)
  {
    request.Id ??= id;
    SetRequest(request);
    IsNew = false;
    _state.ResetSaveOutcome();

    var result = await db.RunMutationAsync(async mutationCt =>
    {
      try
      {
        await SaveCoreAsync(default, new TEntity(), id, request.Ver, (path, value), mutationCt);
      }
      catch (FormRefusedException ex)
      {
        db.MarkMutationRollbackOnly();
        return new GridCellDto
        {
          Outcome = ex.Outcome,
          Messages = ex.Messages.Select(m => m.Path == null
            ? new MessageDto { Detail = m.Detail, Path = path, Code = m.Code, Type = m.Type }
            : m).ToList()
        };
      }
      catch (JsonException)
      {
        db.MarkMutationRollbackOnly();
        return new GridCellDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = L["FormInvalid"], Path = path, Type = MessageType.Error }]
        };
      }

      var row = await readRow(mutationCt);
      return new GridCellDto
      {
        Row = row,
        Outcome = MutationOutcome.Committed,
        Warnings = _state.LastSaveWarnings?.ToList(),
        Affected = _state.LastAffectedParents
      };
    }, ct: ct);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && _state.LastSavedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(_state.LastSavedEntity, db, ct);
    }

    return result;
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
    _state.LastElements = null;

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

    if (Request.ParentId is > 0 && _state.PreloadedParentId != Request.ParentId)
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

        if (Request.ParentId is > 0 && _state.PreloadedParentId != Request.ParentId)
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

  private async Task<(FormSaveContext<TEntity> SaveContext, RuleContext<TEntity> RuleContext)> RunValidationPipelineAsync(
    Elements elements,
    Hooks hooks,
    TEntity entity,
    IReadOnlyDictionary<string, object?>? originalValues,
    IReadOnlySet<string>? presentPaths,
    CancellationToken ct,
    IReadOnlySet<string>? disabledElementPaths = null)
  {
    var saveCtx = new FormSaveContext<TEntity>
    {
      Entity = entity,
      IsNew = IsNew,
      Db = db,
      OriginalValues = originalValues,
      PresentPaths = presentPaths
    };

    foreach (var h in hooks.ComputeActions)
    {
      h(saveCtx);
    }

    var rules = new Rules();
    SetRules(rules);

    var ruleCtx = new RuleContext<TEntity>
    {
      SaveContext = saveCtx
    };

    FormValidator.ValidateElementConstraints(db, L, elements.List, entity, ruleCtx, onlyAcceptingInput: true, disabledElementPaths);

    foreach (var r in rules.Validators)
    {
      await r(saveCtx, ruleCtx, ct);
    }

    await FormValidator.ValidateUniqueElementsAsync(db, L, elements.List, entity, IsNew, ruleCtx, ct);
    await FormValidator.ValidateReferenceElementsAsync(db, PlatformApp, L, elements.List, entity, IsNew, originalValues, ruleCtx, ct, disabledElementPaths);
    return (saveCtx, ruleCtx);
  }

  private TEntity CreateBlankEntity(Parent parent, long? parentId)
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
    Elements elements,
    Parent parent,
    Hooks hooks,
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
    Elements elements,
    Hooks hooks,
    CancellationToken ct)
  {
    await FormReferenceResolver.PopulateJoinedReferencesAsync(db, elements.List, entity, ct);

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
