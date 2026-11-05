using Platform.App.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
{
  public virtual Task<object?> GetModelAsync(long? id, CancellationToken ct = default) =>
    GetModelAsync(new FormRequest { Id = id }, ct);

  public virtual async Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var id = request.Id;
    IsNew = id is null or 0;

    var parent = GetParent();
    await PreloadParentRecordAsync(parent, ct);

    var elements = _lastElements;
    if (elements == null)
    {
      if (!IsNew && CurrentEntity?.Id != id!.Value)
      {
        CurrentEntity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id.Value, ct);
      }

      elements = await EnsureElementsAsync(ct, reload: true);
    }

    TEntity entity;
    if (IsNew)
    {
      entity = CreateBlankEntity(parent, request.ParentId);
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
          if (!string.IsNullOrEmpty(el.ReferencePath))
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
              if (fkPropName != null && PropertyAccessorCache.GetValue(CurrentEntity!, fkPropName) is long and > 0)
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
    _lastSavedEntity = entity;

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
        var targetName = prop.Name;
        var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), targetName);
        if ((entityProp == null || typeof(IEntity).IsAssignableFrom(entityProp.PropertyType)) &&
            !targetName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
            PropertyAccessorCache.GetProperty(typeof(TEntity), targetName + "Id") != null)
        {
          targetName += "Id";
        }

        SetPropertyValue(entity, targetName, prop.Value);
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
      ?? (_lastSavedEntity?.Id is > 0 ? _lastSavedEntity.Id : (long?)null)
      ?? (outcome == null ? Request.Id : null);
    var ver = (entity as IHasVersion)?.Ver
      ?? (_lastSavedEntity as IHasVersion)?.Ver
      ?? (outcome == null && entityId > 0 ? 1 : null);

    var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
    var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
    var props = PropertyAccessorCache.GetProperties(typeof(TEntity))
      .Concat(entity != null && entity.GetType() != typeof(TEntity) ? PropertyAccessorCache.GetProperties(entity.GetType()) : []);
    foreach (var prop in props)
    {
      if (prop.GetCustomAttribute<SensitiveAttribute>() != null)
      {
        var camel = Elements.ToCamelPath(prop.Name);
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

    await PopulateInfoValuesAsync(dict, entity, entityId, ct);

    var unavailable = await EvaluateUnavailableAsync(entityId, ct);

    return new FormDataDto
    {
      Id = outcome == null ? (entityId > 0 ? entityId : null) : entityId,
      Ver = outcome == null ? (entityId > 0 ? ver : null) : ver,
      Value = dict,
      Outcome = outcome,
      Unavailable = unavailable,
      Warnings = !string.IsNullOrEmpty(outcome) ? GetLastSaveWarnings()?.ToList() : null
    };
  }

  public virtual Task<object> SaveAsync(JsonElement json, CancellationToken ct = default) =>
    SaveAsync(json, null, ct);

  public virtual async Task<object> SaveAsync(JsonElement json, FormRequest? request = null, CancellationToken ct = default)
  {
    var (req, entityJson) = FormRequest.Normalize(json, request);
    var input = DeserializeInput(entityJson);

    if (req.Id is null or <= 0 && input.Id > 0)
    {
      req.Id = input.Id;
    }

    var id = req.Id;
    var clientVer = req.Ver;
    Request = req;
    IsNew = id is null or 0;
    _lastSavedEntity = null;
    _lastSaveWarnings = null;

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
        _ => _lastSavedEntity?.Id,
        ct)
      : await db.RunMutationAsync(
        mutationCt => SaveCoreAsync(entityJson, input, id, clientVer, cellPatch: null, mutationCt),
        ct: ct);
    if (PlatformApp != null && _lastSavedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(_lastSavedEntity, db, ct);
    }

    return savedResult;
  }

  private async Task<TEntity> LockAndLoadExistingAsync(long id, Parent parent, CancellationToken ct)
  {
    var requestedParentId = Request.ParentId;
    if (parent.BoundProperty != null &&
        (parent.MatchesService(Request.ParentService) || ResolveParentChildBuilder(requirePreset: true)?.PresetProvider == null))
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

  private static TEntity DeserializeInput(JsonElement entityJson)
  {
    if (entityJson.ValueKind == JsonValueKind.Object && NeedsPropertyDeserialization(entityJson))
    {
      var result = new TEntity();
      foreach (var jProp in entityJson.EnumerateObject())
      {
        var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), jProp.Name);
        var setter = PropertyAccessorCache.GetSetter(typeof(TEntity), jProp.Name);
        if (prop == null || setter == null || IsIgnoredNavigationProperty(prop.PropertyType))
        {
          continue;
        }

        var val = jProp.Value;
        var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (val.ValueKind == JsonValueKind.Object &&
            (targetType == typeof(long) || targetType == typeof(int) || targetType == typeof(string)) &&
            val.TryGetProperty("id", out var idElem))
        {
          val = idElem;
        }

        if (val.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined &&
            prop.PropertyType.IsValueType &&
            Nullable.GetUnderlyingType(prop.PropertyType) == null)
        {
          continue;
        }

        var deserialized = JsonSerializer.Deserialize(val.GetRawText(), prop.PropertyType, PlatformJsonOptions.DefaultOptions);
        setter(result, deserialized);
      }

      return result;
    }

    return JsonSerializer.Deserialize<TEntity>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? new TEntity();
  }

  private static bool NeedsPropertyDeserialization(JsonElement entityJson)
  {
    foreach (var jProp in entityJson.EnumerateObject())
    {
      var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), jProp.Name);
      if (prop == null)
      {
        continue;
      }

      if (jProp.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined &&
          prop.PropertyType.IsValueType &&
          Nullable.GetUnderlyingType(prop.PropertyType) == null)
      {
        return true;
      }

      var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
      if (jProp.Value.ValueKind == JsonValueKind.Object &&
          (targetType == typeof(long) || targetType == typeof(int) || targetType == typeof(string)))
      {
        return true;
      }
    }

    return false;
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

    if (cellPatch is { } cp)
    {
      var targetElement = elements.List.FirstOrDefault(e => string.Equals(e.Path, cp.Path, StringComparison.OrdinalIgnoreCase));
      if (IsReadOnly || targetElement == null || IsElementDisabled(targetElement, entity) || targetElement.IsServerOwned || targetElement.Model is FormInfoModel)
      {
        throw new FormRefusedException(
          MutationOutcome.Refused,
          L["FormReadOnly"],
          [new MessageDto { Detail = L["FormReadOnly"], Path = cp.Path, Type = MessageType.Error }]);
      }
    }
    else if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["FormReadOnly"],
        [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]);
    }

    if (IsNew)
    {
      await InitializeNewRecordAsync(entity, elements, parent, hooks, ct);
    }
    else
    {
      await PopulateExistingRecordAsync(entity, elements, hooks, ct);
      originalValues = SnapshotValues(entity);

      if (cellPatch is { } patch)
      {
        SetPropertyValue(entity, patch.Path, patch.Value);
        NormalizeNullableStrings(elements, entity);
      }
    }

    HashSet<string>? presentPaths = null;
    if (cellPatch is { } cellPatchInfo)
    {
      presentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cellPatchInfo.Path };
    }
    else
    {
      presentPaths = BuildPresentPaths(entityJson);
      NormalizeNullableStrings(elements, input);
      var protectedPaths = BuildProtectedPaths(elements, presentPaths, IsNew, entity, input);
      CopyMatchingProperties(input, entity, protectedPaths);
      NormalizeNullableStrings(elements, entity);
      if (id.HasValue && id.Value > 0)
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

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, originalValues, presentPaths, ct);
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

    ValidateElementConstraints(elements, entity, ruleCtx, onlyAcceptingInput: false);
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

    await SyncJoinedReferencesAsync(elements, entity, ct);

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
      await ParentScopeValidator.RecalculateBoundParentsAsync(
        db,
        PlatformApp,
        Request.ParentService,
        parent.BoundParentService,
        parent.BoundParentServiceType,
        [oldParentId, newParentId],
        ct);
    }

    _lastSavedEntity = entity;
    _lastSaveWarnings = saveCtx.Warnings.Count > 0 ? [.. saveCtx.Warnings] : null;
    return entity;
  }

  public virtual async Task<GridCellDto> SaveCellAsync(
    long id,
    string path,
    JsonElement value,
    FormRequest request,
    Func<CancellationToken, Task<object?>> readRow,
    CancellationToken ct = default)
  {
    request.Id ??= id;
    Request = request;
    IsNew = false;
    _lastSavedEntity = null;
    _lastSaveWarnings = null;

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
          Warnings = ex.Messages.Select(m => m.Path == null
            ? new MessageDto { Detail = m.Detail, Path = path, Code = m.Code, Type = m.Type }
            : m).ToList()
        };
      }

      var row = await readRow(mutationCt);
      return new GridCellDto
      {
        Row = row,
        Outcome = MutationOutcome.Committed,
        Warnings = _lastSaveWarnings?.ToList()
      };
    }, ct: ct);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && _lastSavedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(_lastSavedEntity, db, ct);
    }

    return result;
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
    await PopulateJoinedReferencesAsync(elements, entity, ct);

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

  private async Task PopulateJoinedReferencesAsync(
    Elements elements,
    TEntity entity,
    CancellationToken ct)
  {
    foreach (var el in elements.List)
    {
      if (el.JoinLoader == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var ids = await el.JoinLoader(db, entity.Id, ct);
      var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (entityProp != null && entityProp.CanWrite && entityProp.PropertyType.IsAssignableFrom(typeof(List<long>)))
      {
        entityProp.SetValue(entity, new List<long>(ids));
      }
    }
  }

  private async Task SyncJoinedReferencesAsync(
    Elements elements,
    TEntity entity,
    CancellationToken ct)
  {
    var anyChanged = false;
    foreach (var el in elements.List)
    {
      if (el.JoinSyncer == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var raw = PropertyAccessorCache.GetValue(entity, el.Path);
      var desired = new List<long>();
      if (raw is IEnumerable<long> longSeq)
      {
        desired = longSeq.Where(id => id > 0).Distinct().ToList();
      }
      else if (raw is IEnumerable enumerable and not string)
      {
        foreach (var item in enumerable)
        {
          if (item != null && long.TryParse(item.ToString(), out var parsed) && parsed > 0 && !desired.Contains(parsed))
          {
            desired.Add(parsed);
          }
        }
      }

      if (await el.JoinSyncer(db, entity.Id, desired, ct))
      {
        anyChanged = true;
      }

      var entityProp = PropertyAccessorCache.GetProperty(typeof(TEntity), el.Path);
      if (entityProp != null && entityProp.CanWrite && entityProp.PropertyType.IsAssignableFrom(typeof(List<long>)))
      {
        entityProp.SetValue(entity, new List<long>(desired));
      }
    }

    if (anyChanged)
    {
      await db.SaveChangesAsync(ct);
    }
  }
}
