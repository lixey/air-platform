using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormSavePipeline
{
  public static async Task<object> SaveAsync<TEntity>(
    FormService<TEntity> form,
    JsonElement json,
    FormRequest? request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var (req, entityJson) = FormRequest.Normalize(json, request);
    if (req.Value is not { ValueKind: JsonValueKind.Object })
    {
      throw new JsonException("FormRequest 'value' object is required.");
    }

    var input = FormValueBinder.DeserializeInput<TEntity>(entityJson);

    var id = req.Id;
    var clientVer = req.Ver;
    var db = form.DbContext;
    form.SetRequest(req);
    form.IsNewRecord = id is null or 0;
    form.State.ResetSaveOutcome();

    var savedResult = form.IsNewRecord
      ? await MutationRunner.RunIdempotentCreateAsync<object>(
        db,
        form.Service,
        req.IdempotencyKey,
        req.RawPayload ?? json.GetRawText(),
        mutationCt => SaveCoreAsync(form, entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        async (replayedId, reloadCt) =>
        {
          form.CurrentRequest.Id = replayedId;
          return await form.GetModelAsync(form.CurrentRequest, reloadCt)
            ?? await form.ReachableQuery(asTracking: true).FirstOrDefaultAsync(e => e.Id == replayedId, reloadCt);
        },
        _ => form.State.LastSavedEntity?.Id,
        ct,
        form.PlatformApp?.Services)
      : await db.RunMutationAsync(
        mutationCt => SaveCoreAsync(form, entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        ct: ct);

    if (form.PlatformApp != null && form.State.LastSavedEntity != null)
    {
      await form.PlatformApp.ClearAuthCacheForEntityAsync(form.State.LastSavedEntity, db, ct);
    }

    return savedResult;
  }

  public static async Task<TEntity> LockAndLoadExistingAsync<TEntity>(
    FormService<TEntity> form,
    long id,
    FormService<TEntity>.Parent parent,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var db = form.DbContext;
    var requestedParentId = form.CurrentRequest.ParentId;
    if (parent.BoundProperty != null &&
        (parent.MatchesService(form.CurrentRequest.ParentService) || (await form.ResolveParentChildBuilderAsync(requirePreset: true, ct))?.PresetProvider == null))
    {
      var probe = await db.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
      if (probe != null)
      {
        var probeParentId = parent.BoundProperty.GetValue(probe) as long?;
        if (requestedParentId is > 0 && probeParentId is > 0 && probeParentId.Value != requestedParentId.Value)
        {
          throw new FormRefusedException(MutationOutcome.Refused, form.L["MutationRefused"], [new MessageDto { Detail = form.L["FormNotFound"], Type = MessageType.Error }]);
        }

        if (!parent.IsOptional || !form.CurrentRequest.ParentId.HasValue)
        {
          if (probeParentId.HasValue)
          {
            form.CurrentRequest.ParentId = probeParentId.Value;
          }
        }
      }
    }

    var lockedParentId = form.CurrentRequest.ParentId;
    await form.EnsureParentScopeInternalAsync(parent, lockParent: true, ct);
    await MutationContext.AcquireLocksAsync(db, typeof(TEntity), [[id]], ct);

    var entity = await form.ReachableQuery(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct)
      ?? throw new KeyNotFoundException(form.L["FormNotFound"]);

    form.CurrentRequest.ParentId = requestedParentId;
    form.RestoreBoundParentId(entity, parent);
    if (form.CurrentRequest.ParentId != lockedParentId)
    {
      await form.EnsureParentScopeInternalAsync(parent, lockParent: true, ct);
    }

    return entity;
  }

  private static async Task<object> SaveCoreAsync<TEntity>(
    FormService<TEntity> form,
    JsonElement entityJson,
    TEntity input,
    long? id,
    int? clientVer,
    (string Path, JsonElement Value)? cellPatch,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var db = form.DbContext;
    var parent = form.GetParentDeclaration();
    var hooks = form.GetHooksDeclaration();

    TEntity entity;
    Dictionary<string, object?>? originalValues = null;
    if (form.IsNewRecord)
    {
      if (form.CurrentRequest.ParentId is null or <= 0 &&
          parent.BoundProperty != null &&
          PropertyAccessorCache.GetValue(input, parent.BoundProperty.Name) is long inputPid and > 0)
      {
        form.CurrentRequest.ParentId = inputPid;
      }

      await form.EnsureParentScopeInternalAsync(parent, lockParent: true, ct);
      entity = FormModelLoader.CreateBlankEntity(form, parent, form.CurrentRequest.ParentId);
      if (parent.BoundProperty != null && !parent.IsOptional)
      {
        var boundParentVal = PropertyAccessorCache.GetValue(entity, parent.BoundProperty.Name)
          ?? PropertyAccessorCache.GetValue(input, parent.BoundProperty.Name);
        if (boundParentVal is not long pid || pid <= 0)
        {
          throw new FormRefusedException(
            MutationOutcome.Refused,
            form.L["FormInvalid"],
            [new MessageDto
            {
              Path = Elements.ToCamelPath(parent.BoundProperty.Name),
              Detail = form.L["Required"],
              Code = "form.validation.required",
              Type = MessageType.Error
            }]);
        }
      }
    }
    else
    {
      entity = await LockAndLoadExistingAsync(form, id!.Value, parent, ct);

      if (entity is IHasVersion versionedEntity &&
          (!clientVer.HasValue || clientVer.Value <= 0 || versionedEntity.Ver != clientVer.Value))
      {
        throw new FormRefusedException(
          MutationOutcome.Conflict,
          form.L["MutationConflict"],
          [new MessageDto { Detail = form.L["FormRecordChanged"], Path = cellPatch?.Path, Type = MessageType.Error }]);
      }
    }

    form.Entity = entity;
    var elements = await form.EnsureElementsAsync(ct, reload: true);

    if (form.IsFormReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        form.L["FormReadOnly"],
        [new MessageDto { Detail = form.L["FormReadOnly"], Path = cellPatch?.Path, Type = MessageType.Error }]);
    }

    if (form.IsNewRecord)
    {
      await FormModelLoader.InitializeNewRecordAsync(form, entity, elements, parent, hooks, ct);
    }
    else
    {
      await FormModelLoader.PopulateExistingRecordAsync(form, entity, elements, hooks, ct);
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
          form.L["FormReadOnly"],
          [new MessageDto { Detail = form.L["FormReadOnly"], Path = cp.Path, Type = MessageType.Error }]);
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
        form.IsNewRecord,
        entity,
        input,
        presentPaths);
      var protectedPaths = FormValueBinder.BuildProtectedPaths(
        elements.List,
        parent.BoundProperty,
        parent.IsOptional,
        presentPaths,
        form.IsNewRecord,
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
          form.L["FormReadOnly"],
          [new MessageDto { Detail = form.L["FormReadOnly"], Path = cp.Path, Type = MessageType.Error }]);
      }

      FormValueBinder.SetPropertyValue(entity, effectiveCellPath, cp.Value);
      FormValueBinder.NormalizeNullableStrings(elements.List, entity);
      if (await FormSelectPatcher.ApplyServerSelectPatchesAsync(
            db,
            form.PlatformApp,
            elements.List,
            parent.BoundProperty,
            parent.IsOptional,
            form.IsNewRecord,
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
          form.IsNewRecord,
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
        form.IsNewRecord,
        entity,
        input,
        presentPaths);
      var protectedPaths = FormValueBinder.BuildProtectedPaths(
        elements.List,
        parent.BoundProperty,
        parent.IsOptional,
        presentPaths,
        form.IsNewRecord,
        entity,
        input,
        disabledElementPaths);
      FormValueBinder.CopyMatchingProperties(input, entity, protectedPaths);
      FormValueBinder.NormalizeNullableStrings(elements.List, entity);
      if (await FormSelectPatcher.ApplyServerSelectPatchesAsync(
            db,
            form.PlatformApp,
            elements.List,
            parent.BoundProperty,
            parent.IsOptional,
            form.IsNewRecord,
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
          form.IsNewRecord,
          entity,
          entity,
          presentPaths);
      }

      if (id is > 0)
      {
        entity.Id = id.Value;
      }

      if (form.IsNewRecord)
      {
        form.ApplyBoundParentId(entity, parent, form.CurrentRequest.ParentId);
      }

      if (parent.BoundProperty != null && !parent.IsOptional)
      {
        var boundParentVal = PropertyAccessorCache.GetValue(entity, parent.BoundProperty.Name);
        if (boundParentVal is not long pid || pid <= 0)
        {
          throw new FormRefusedException(
            MutationOutcome.Refused,
            form.L["FormInvalid"],
            [new MessageDto
            {
              Path = Elements.ToCamelPath(parent.BoundProperty.Name),
              Detail = form.L["Required"],
              Code = "form.validation.required",
              Type = MessageType.Error
            }]);
        }
      }
    }

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(form, elements, hooks, entity, originalValues, presentPaths, ct, disabledElementPaths);
    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, form.L["FormInvalid"], saveCtx.Errors);
    }

    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, form.L["FormInvalid"], ruleCtx.Failures);
    }

    foreach (var h in hooks.BeforeSaveActions)
    {
      await h(saveCtx, ct);
    }

    if (saveCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, form.L["FormInvalid"], saveCtx.Errors);
    }

    FormValidator.ValidateElementConstraints(db, form.L, elements.List, entity, ruleCtx, onlyAcceptingInput: false, disabledElementPaths);
    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, form.L["FormInvalid"], ruleCtx.Failures);
    }

    if (form.IsNewRecord)
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
        throw new FormRefusedException(MutationOutcome.Refused, form.L["FormInvalid"], saveCtx.Errors);
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(ct);
      }
    }

    if (parent.BoundProperty != null)
    {
      var oldParentId = !form.IsNewRecord && originalValues != null && originalValues.TryGetValue(parent.BoundProperty.Name, out var oldVal) && oldVal is long oldPid
        ? oldPid
        : (long?)null;
      var newParentId = parent.BoundProperty.GetValue(entity) as long? ?? form.CurrentRequest.ParentId;
      form.State.LastAffectedParents = await ParentScopeValidator.RecalculateBoundParentsAsync(
        db,
        form.PlatformApp,
        form.CurrentRequest.ParentService,
        parent.BoundParentService,
        parent.BoundParentServiceType,
        [oldParentId, newParentId],
        ct,
        requestParentId: form.CurrentRequest.ParentId);
    }

    form.State.LastSavedEntity = entity;
    form.State.LastSaveWarnings = saveCtx.Warnings.Count > 0 ? [.. saveCtx.Warnings] : null;
    return entity;
  }

  public static async Task<GridCellDto> SaveCellAsync<TEntity>(
    FormService<TEntity> form,
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<RowDto?>> readRow,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    request.Id ??= id;
    form.SetRequest(request);
    form.IsNewRecord = false;
    form.State.ResetSaveOutcome();
    var db = form.DbContext;

    var result = await db.RunMutationAsync(async mutationCt =>
    {
      try
      {
        await SaveCoreAsync(form, default, new TEntity(), id, request.Ver, (path, value), mutationCt);
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
          Messages = [new MessageDto { Detail = form.L["FormInvalid"], Path = path, Type = MessageType.Error }]
        };
      }

      var row = await readRow(mutationCt);
      return new GridCellDto
      {
        Row = row,
        Outcome = MutationOutcome.Committed,
        Warnings = form.State.LastSaveWarnings?.ToList(),
        Affected = form.State.LastAffectedParents
      };
    }, ct: ct);

    if (result.Outcome == MutationOutcome.Committed && form.PlatformApp != null && form.State.LastSavedEntity != null)
    {
      await form.PlatformApp.ClearAuthCacheForEntityAsync(form.State.LastSavedEntity, db, ct);
    }

    return result;
  }

  public static async Task<(FormSaveContext<TEntity> SaveContext, RuleContext<TEntity> RuleContext)> RunValidationPipelineAsync<TEntity>(
    FormService<TEntity> form,
    FormService<TEntity>.Elements elements,
    FormService<TEntity>.Hooks hooks,
    TEntity entity,
    IReadOnlyDictionary<string, object?>? originalValues,
    IReadOnlySet<string>? presentPaths,
    CancellationToken ct,
    IReadOnlySet<string>? disabledElementPaths = null)
    where TEntity : class, IEntity, new()
  {
    var db = form.DbContext;
    var saveCtx = new FormSaveContext<TEntity>
    {
      Entity = entity,
      IsNew = form.IsNewRecord,
      Db = db,
      OriginalValues = originalValues,
      PresentPaths = presentPaths
    };

    foreach (var h in hooks.ComputeActions)
    {
      h(saveCtx);
    }

    var rules = new FormService<TEntity>.Rules();
    form.InvokeSetRules(rules);

    var ruleCtx = new RuleContext<TEntity>
    {
      SaveContext = saveCtx
    };

    FormValidator.ValidateElementConstraints(db, form.L, elements.List, entity, ruleCtx, onlyAcceptingInput: true, disabledElementPaths);

    foreach (var r in rules.Validators)
    {
      await r(saveCtx, ruleCtx, ct);
    }

    await FormValidator.ValidateUniqueElementsAsync(db, form.L, elements.List, entity, form.IsNewRecord, ruleCtx, ct);
    await FormValidator.ValidateReferenceElementsAsync(db, form.PlatformApp, form.L, elements.List, entity, form.IsNewRecord, originalValues, ruleCtx, ct, disabledElementPaths);
    return (saveCtx, ruleCtx);
  }
}
