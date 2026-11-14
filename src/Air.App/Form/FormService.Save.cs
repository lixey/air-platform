using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
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
    State.ResetSaveOutcome();

    var savedResult = IsNew
      ? await MutationRunner.RunIdempotentCreateAsync<object>(
        db,
        Service,
        req.IdempotencyKey,
        !string.IsNullOrWhiteSpace(req.IdempotencyKey) ? req.RawPayload ?? json.GetRawText() : string.Empty,
        mutationCt => SaveCoreAsync(entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        async (replayedId, reloadCt) =>
        {
          Request.Id = replayedId;
          return await GetModelAsync(Request, reloadCt)
            ?? await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == replayedId, reloadCt);
        },
        _ => State.LastSavedEntity?.Id,
        ct,
        PlatformApp?.Services)
      : await db.RunMutationAsync(
        mutationCt => SaveCoreAsync(entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        ct: ct);

    if (PlatformApp != null && State.LastSavedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(State.LastSavedEntity, db, ct);
    }

    return savedResult;
  }

  public virtual async Task<GridCellDto> SaveCellAsync(
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<RowDto?>> readRow,
    Func<CancellationToken, Task<bool>>? authorizeRow = null,
    CancellationToken ct = default)
  {
    request.Id ??= id;
    SetRequest(request);
    IsNew = false;
    State.ResetSaveOutcome();
    var rawPayload = request.RawPayload ?? $"{request.ParentService}|{request.ParentId}|{id}|{request.Ver}|{path}|{value.GetRawText()}";

    var result = await MutationRunner.RunIdempotentMutationAsync<GridCellDto>(
      db,
      $"{Service}/save-cell",
      request.IdempotencyKey,
      rawPayload,
      async mutationCt =>
      {
        if (authorizeRow != null && !await authorizeRow(mutationCt))
        {
          return new GridCellDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Path = path, Type = MessageType.Error }]
          };
        }

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
          Warnings = State.LastSaveWarnings?.ToList(),
          Affected = State.LastAffectedParents
        };
      },
      ct,
      PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && State.LastSavedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(State.LastSavedEntity, db, ct);
    }

    return result;
  }

  private async Task<TEntity> LockAndLoadExistingAsync(
    long id,
    FormParent<TEntity> parent,
    CancellationToken ct) =>
    (await LockAndLoadExistingAsync<object>(id, parent, beforeEntityLock: null, ct)).Entity!;

  private async Task<(TEntity? Entity, TShortCircuit? Refusal)> LockAndLoadExistingAsync<TShortCircuit>(
    long id,
    FormParent<TEntity> parent,
    Func<TEntity, CancellationToken, Task<(bool Continue, TShortCircuit? Refusal)>>? beforeEntityLock,
    CancellationToken ct)
  {
    var requestedParentId = Request.ParentId;
    var needsBoundParentProbe = parent.BoundProperty != null &&
      (parent.MatchesService(Request.ParentService) || (await ResolveParentChildBuilderAsync(requirePreset: true, ct))?.PresetProvider == null);

    TEntity? probe = null;
    if (needsBoundParentProbe || beforeEntityLock != null)
    {
      probe = await UnscopedReachable(asTracking: false).FirstOrDefaultAsync(e => e.Id == id, ct);
      if (beforeEntityLock != null && probe == null)
      {
        throw new KeyNotFoundException(L["FormNotFound"]);
      }

      if (needsBoundParentProbe && probe != null)
      {
        var probeParentId = parent.BoundProperty!.GetValue(probe) as long?;
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

    if (beforeEntityLock != null)
    {
      var (shouldContinue, refusal) = await beforeEntityLock(probe!, ct);
      if (!shouldContinue)
      {
        return (null, refusal);
      }
    }

    await MutationContext.AcquireLocksAsync(db, typeof(TEntity), [[id]], ct);

    if (db.Set<TEntity>().Local.FirstOrDefault(e => e.Id == id) is { } localBeforeLock &&
        db.Entry(localBeforeLock).State == EntityState.Unchanged &&
        db.Database.IsRelational())
    {
      await db.Entry(localBeforeLock).ReloadAsync(ct);
    }

    var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    Request.ParentId = requestedParentId;
    RestoreBoundParentId(entity, parent);
    if (Request.ParentId != lockedParentId)
    {
      await EnsureParentScopeAsync(parent, lockParent: true, ct);
    }

    return (entity, default);
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
              Path = Form.Elements.ToCamelPath(parent.BoundProperty.Name),
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
      FormValueBinder.NormalizeRightsTrees(elements.List, entity);
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
      FormValueBinder.NormalizeRightsTrees(elements.List, entity);
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
              Path = Form.Elements.ToCamelPath(parent.BoundProperty.Name),
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
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.ToErrorMessages());
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
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.ToErrorMessages());
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
    await SyncJoinedReferencesAsync(elements.List, entity, ct);

    if (hooks.AfterSaveActions.Count > 0)
    {
      foreach (var h in hooks.AfterSaveActions)
      {
        await h(saveCtx, ct);
      }

      if (saveCtx.Errors.Count > 0)
      {
        throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.ToErrorMessages());
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
      State.LastAffectedParents = await BoundParentRecalculator.RecalculateBoundParentsAsync(
        db,
        PlatformApp,
        Request.ParentService,
        parent.BoundParentService,
        parent.BoundParentServiceType,
        [oldParentId, newParentId],
        ct,
        requestParentId: Request.ParentId);
    }

    State.LastSavedEntity = entity;
    State.LastSaveWarnings = saveCtx.Warnings.Count > 0 ? saveCtx.ToWarningMessages() : null;
    return entity;
  }

  private async Task<(FormSaveContext<TEntity> SaveContext, RuleContext<TEntity> RuleContext)> RunValidationPipelineAsync(
    FormElements<TEntity> elements,
    FormHooks<TEntity> hooks,
    TEntity entity,
    IReadOnlyDictionary<string, object?>? originalValues,
    IReadOnlySet<string>? presentPaths,
    CancellationToken ct,
    IReadOnlySet<string>? disabledElementPaths = null)
  {
    var saveCtx = new FormSaveContext<TEntity>(db)
    {
      Entity = entity,
      IsNew = IsNew,
      OriginalValues = originalValues,
      PresentPaths = presentPaths
    };

    foreach (var h in hooks.ComputeActions)
    {
      h(saveCtx);
    }

    var rules = BuildRules();

    var ruleCtx = new RuleContext<TEntity>
    {
      SaveContext = saveCtx,
      Localizer = L
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
}
