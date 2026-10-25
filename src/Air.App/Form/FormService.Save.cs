namespace Platform.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  public virtual Task<object?> GetModelAsync(long? id, CancellationToken ct = default) =>
    GetModelAsync(new FormRequest { Id = id }, ct);

  public virtual async Task<object?> GetModelAsync(FormRequest request, CancellationToken ct = default)
  {
    Request = request;
    var id = request.Id;
    IsNew = id is null or 0;

    var parent = GetParent();

    var elements = _lastElements;
    if (elements == null)
    {
      elements = new Elements();
      await SetElementsAsync(elements, ct);
      LocalizeAndResolveSelectElements(elements);
      _lastElements = elements;
    }

    TEntity entity;
    if (IsNew)
    {
      entity = new TEntity();
      if (entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
      {
        tenantEntity.TenantId = tid;
      }

      ApplyBoundParentId(entity, parent, request.ParentId);
      if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }
    }
    else
    {
      var query = Reachable(asTracking: true);
      var efModel = db.Model.FindEntityType(typeof(TEntity));
      if (efModel != null)
      {
        var refNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
          }
        }
      }

      entity = await query.FirstOrDefaultAsync(e => e.Id == id!.Value, ct)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);

      RestoreBoundParentId(entity, parent);
    }

    _lastSavedEntity = entity;

    var input = entity is TInput same ? same : new TInput();
    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

    if (!IsNew)
    {
      await PopulateJoinedReferencesAsync(elements, entity, input, ct);
    }

    var hooks = GetHooks();

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = IsNew
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
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
        if (!ReferenceEquals(entity, input))
        {
          SetPropertyValue(input, targetName, prop.Value);
        }
      }
    }

    return input;
  }

  public virtual async Task<FormDataDto> BuildFormDataAsync(
    object? entity,
    ScreenDto? screen = null,
    string? outcome = null,
    CancellationToken ct = default)
  {
    var entityId = (entity as IEntity)?.Id
      ?? (_lastSavedEntity?.Id is > 0 ? _lastSavedEntity.Id : (long?)null)
      ?? (outcome == null ? Request.Id : null);
    var ver = (entity as IHasVersion)?.Ver
      ?? (_lastSavedEntity as IHasVersion)?.Ver
      ?? (outcome == null && entityId > 0 ? 1 : null);

    var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
    var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
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
    var entityJson = json.ValueKind == JsonValueKind.Object && (json.TryGetProperty("value", out var val) || json.TryGetProperty("entity", out val))
      ? val
      : json;

    var input = JsonSerializer.Deserialize<TInput>(entityJson.GetRawText(), PlatformJsonOptions.DefaultOptions)
      ?? new TInput();

    var id = request?.Id;
    if (id is null or <= 0 && input is IEntity inpEntity && inpEntity.Id > 0)
    {
      id = inpEntity.Id;
    }
    else if (id is null or <= 0 && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("id", out var idProp))
    {
      if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var rootId) && rootId > 0)
      {
        id = rootId;
      }
      else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var strId) && strId > 0)
      {
        id = strId;
      }
    }

    var clientVer = request?.Ver;
    if (clientVer is null && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("ver", out var verProp) && verProp.ValueKind == JsonValueKind.Number && verProp.TryGetInt32(out var rootVer))
    {
      clientVer = rootVer;
    }
    else if (clientVer is null && entityJson.ValueKind == JsonValueKind.Object && entityJson.TryGetProperty("ver", out var entityVerProp) && entityVerProp.ValueKind == JsonValueKind.Number && entityVerProp.TryGetInt32(out var entityVer) && entityVer > 0)
    {
      clientVer = entityVer;
    }

    request ??= new FormRequest();
    request.Id = id;
    request.Ver = clientVer;
    if (!request.ParentId.HasValue && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var jsonPid))
    {
      request.ParentId = jsonPid;
    }

    if (string.IsNullOrEmpty(request.ParentService) && json.ValueKind == JsonValueKind.Object && json.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } jsonPs)
    {
      request.ParentService = jsonPs;
    }

    Request = request;
    IsNew = id is null or 0;

    var parent = GetParent();

    var hooks = GetHooks();

    TEntity entity;
    Dictionary<string, object?>? originalValues = null;
    if (IsNew)
    {
      entity = new TEntity();
      if (entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
      {
        tenantEntity.TenantId = tid;
      }

      ApplyBoundParentId(entity, parent, Request.ParentId);
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
      entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id!.Value, ct)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);

      if (clientVer.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != clientVer.Value)
      {
        throw new FormRefusedException(
          MutationOutcome.Conflict,
          L["MutationConflict"],
          [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]);
      }

      RestoreBoundParentId(entity, parent);
    }

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    LocalizeAndResolveSelectElements(elements);
    _lastElements = elements;

    if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["FormReadOnly"],
        [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]);
    }

    if (IsNew)
    {
      if (elements.StatusFlow is IRecordStatuses<TEntity> statusFlow && statusFlow.Initial != null)
      {
        statusFlow.Initialize(entity);
      }

      var newLoadInput = new TInput();
      CopyMatchingProperties(entity, newLoadInput);
      var newLoadCtx = new FormLoadContext<TEntity, TInput>
      {
        Entity = entity,
        Input = newLoadInput,
        IsNew = true
      };
      foreach (var h in hooks.AfterLoadActions)
      {
        await h(newLoadCtx, ct);
      }
    }
    else
    {
      var baselineInput = new TInput();
      CopyMatchingProperties(entity, baselineInput);
      await PopulateJoinedReferencesAsync(elements, entity, baselineInput, ct);
      var baselineLoadCtx = new FormLoadContext<TEntity, TInput>
      {
        Entity = entity,
        Input = baselineInput,
        IsNew = false
      };
      foreach (var h in hooks.AfterLoadActions)
      {
        await h(baselineLoadCtx, ct);
      }

      originalValues = SnapshotValues(baselineInput);
    }

    NormalizeNullableStrings(elements, input);
    var protectedPaths = BuildProtectedPaths(elements, entityJson, IsNew);
    var inputOnlyPaths = BuildInputOnlyPaths(elements);
    CopyMatchingProperties(input, entity, protectedPaths);
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

    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input, inputOnlyPaths);
    }

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, input, originalValues, ct);
    if (ruleCtx.Failures.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], ruleCtx.Failures);
    }

    await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      foreach (var h in hooks.BeforeSaveActions)
      {
        await h(saveCtx, ct);
      }

      if (saveCtx.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
      }

      if (!ReferenceEquals(entity, input))
      {
        CopyMatchingProperties(entity, input, inputOnlyPaths);
      }

      ValidateElementConstraints(elements, entity, input, ruleCtx, onlyAcceptingInput: false);
      if (ruleCtx.Failures.Count > 0)
      {
        await tx.RollbackAsync(ct);
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

      await SyncJoinedReferencesAsync(elements, entity, input, ct);

      if (hooks.AfterSaveActions.Count > 0)
      {
        foreach (var h in hooks.AfterSaveActions)
        {
          await h(saveCtx, ct);
        }

        if (saveCtx.Errors.Count > 0)
        {
          await tx.RollbackAsync(ct);
          throw new FormRefusedException(MutationOutcome.Refused, L["FormInvalid"], saveCtx.Errors);
        }

        if (db.ChangeTracker.HasChanges())
        {
          await db.SaveChangesAsync(ct);
        }
      }

      await tx.CommitAsync(ct);
    });

    _lastSavedEntity = entity;
    _lastSaveWarnings = saveCtx.Warnings.Count > 0 ? [.. saveCtx.Warnings] : null;

    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

    return input;
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

    var parent = GetParent();

    var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    if (request.Ver.HasValue && entity is IHasVersion versionedEntity && versionedEntity.Ver != request.Ver.Value)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Path = path, Type = MessageType.Error }]
      };
    }

    RestoreBoundParentId(entity, parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    LocalizeAndResolveSelectElements(elements);

    var targetElement = elements.List.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
    if (IsReadOnly || targetElement == null || targetElement.IsDisabled || targetElement.IsServerOwned || targetElement.Model is FormInfoModel)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormReadOnly"], Path = path, Type = MessageType.Error }]
      };
    }

    var input = entity is TInput same ? same : new TInput();
    if (!ReferenceEquals(entity, input))
    {
      CopyMatchingProperties(entity, input);
    }

    if (!IsNew)
    {
      await PopulateJoinedReferencesAsync(elements, entity, input, ct);
    }

    var hooks = GetHooks();

    var loadCtx = new FormLoadContext<TEntity, TInput>
    {
      Entity = entity,
      Input = input,
      IsNew = false
    };

    foreach (var h in hooks.AfterLoadActions)
    {
      await h(loadCtx, ct);
    }

    var originalValues = SnapshotValues(input);

    SetPropertyValue(input, path, value);
    if (!ReferenceEquals(entity, input))
    {
      SetPropertyValue(entity, path, value);
    }

    NormalizeNullableStrings(elements, input);
    if (!ReferenceEquals(entity, input))
    {
      NormalizeNullableStrings(elements, entity);
    }

    var (saveCtx, ruleCtx) = await RunValidationPipelineAsync(elements, hooks, entity, input, originalValues, ct);
    if (ruleCtx.Failures.Count > 0)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = ruleCtx.Failures.Select(f => new MessageDto
        {
          Detail = f.Detail,
          Path = f.Path ?? path,
          Code = f.Code,
          Type = MessageType.Error
        }).ToList()
      };
    }

    var cellRefused = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      foreach (var h in hooks.BeforeSaveActions)
      {
        await h(saveCtx, ct);
      }

      if (saveCtx.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return true;
      }

      ValidateElementConstraints(elements, entity, input, ruleCtx, onlyAcceptingInput: false);
      if (ruleCtx.Failures.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return true;
      }

      if (db.Entry(entity).State == EntityState.Detached)
      {
        db.Set<TEntity>().Update(entity);
      }

      await db.SaveChangesAsync(ct);

      await SyncJoinedReferencesAsync(elements, entity, input, ct);

      if (hooks.AfterSaveActions.Count > 0)
      {
        foreach (var h in hooks.AfterSaveActions)
        {
          await h(saveCtx, ct);
        }

        if (saveCtx.Errors.Count > 0)
        {
          await tx.RollbackAsync(ct);
          return true;
        }

        if (db.ChangeTracker.HasChanges())
        {
          await db.SaveChangesAsync(ct);
        }
      }

      await tx.CommitAsync(ct);
      return false;
    });

    if (cellRefused)
    {
      return new GridCellDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = saveCtx.Errors.Count > 0
          ? saveCtx.Errors
          : ruleCtx.Failures.Select(f => new MessageDto
          {
            Detail = f.Detail,
            Path = f.Path ?? path,
            Code = f.Code,
            Type = MessageType.Error
          }).ToList()
      };
    }

    var row = await readRow(ct);
    return new GridCellDto
    {
      Row = row,
      Outcome = MutationOutcome.Committed,
      Warnings = saveCtx.Warnings.Count > 0 ? saveCtx.Warnings : null
    };
  }

  public virtual async Task DeleteAsync(long id, CancellationToken ct = default)
  {
    if (!Feature.HasDelete || User != null && User.IsAuthenticated && !User.Can(Feature.Delete) && !User.Can(Feature.All))
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]);
    }

    Request = new FormRequest { Id = id };
    IsNew = false;

    var parent = GetParent();

    var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    RestoreBoundParentId(entity, parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);

    if (IsReadOnly)
    {
      throw new FormRefusedException(
        MutationOutcome.Refused,
        L["MutationRefused"],
        [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]);
    }

    var hooks = GetHooks();

    var deleteCtx = new FormDeleteContext<TEntity> { Entity = entity };
    foreach (var h in hooks.BeforeDeleteActions)
    {
      await h(deleteCtx, ct);
    }

    if (deleteCtx.Errors.Count > 0)
    {
      throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], deleteCtx.Errors);
    }

    await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      foreach (var el in elements.List)
      {
        if (el.JoinSyncer != null)
        {
          await el.JoinSyncer(db, entity.Id, [], ct);
        }
      }

      db.Set<TEntity>().Remove(entity);
      await db.SaveChangesAsync(ct);

      if (hooks.AfterDeleteActions.Count > 0)
      {
        foreach (var h in hooks.AfterDeleteActions)
        {
          await h(deleteCtx, ct);
        }

        if (deleteCtx.Errors.Count > 0)
        {
          await tx.RollbackAsync(ct);
          throw new FormRefusedException(MutationOutcome.Refused, L["MutationRefused"], deleteCtx.Errors);
        }

        if (db.ChangeTracker.HasChanges())
        {
          await db.SaveChangesAsync(ct);
        }
      }

      await tx.CommitAsync(ct);
    });
  }

  public virtual async Task<FormActionDto> MoveStatusAsync(
    long id,
    string transitionName,
    JsonElement? body,
    int? ver = null,
    CancellationToken ct = default)
  {
    Request = new FormRequest { Id = id, Ver = ver };
    IsNew = false;

    var parent = GetParent();

    var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, ct)
      ?? throw new KeyNotFoundException(L["FormNotFound"]);

    RestoreBoundParentId(entity, parent);

    var elements = new Elements();
    await SetElementsAsync(elements, ct);
    if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
    }

    if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
    {
      flowSvc.AttachPlatformApp(PlatformApp);
    }

    if (ver.HasValue && ver.Value > 0 && entity is IHasVersion versioned && versioned.Ver != ver.Value)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Conflict,
        Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
      };
    }

    var transition = statusFlow.Transitions.FirstOrDefault(t =>
      string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidOperationException($"Transition '{transitionName}' not found in '{statusFlow.GetType().Name}'.");

    if (User != null && User.IsAuthenticated && !User.Can(Feature.Action(transition.Name)) && !User.Can(Feature.Edit) && !User.Can(Feature.All))
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
      };
    }

    var input = transition.Ask?.Read(body);
    if (transition.Ask != null)
    {
      var askFailures = transition.Ask.Validate(input, L);
      if (askFailures.Count > 0)
      {
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [.. askFailures]
        };
      }
    }

    return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync(ct);
      var context = new MutationContext(db);
      await statusFlow.MoveAsync(context, [entity], transition.Name, input, ct);

      if (context.Errors.Count > 0)
      {
        await tx.RollbackAsync(ct);
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
        };
      }

      await tx.CommitAsync(ct);
      return new FormActionDto
      {
        Outcome = MutationOutcome.Committed,
        Warnings = context.Warnings.Count > 0 ? context.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList() : null
      };
    });
  }

  private async Task PopulateJoinedReferencesAsync(
    Elements elements,
    TEntity entity,
    TInput input,
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

      if (!ReferenceEquals(entity, input))
      {
        var inputProp = PropertyAccessorCache.GetProperty(typeof(TInput), el.Path);
        if (inputProp != null && inputProp.CanWrite && inputProp.PropertyType.IsAssignableFrom(typeof(List<long>)))
        {
          inputProp.SetValue(input, new List<long>(ids));
        }
      }
    }
  }

  private async Task SyncJoinedReferencesAsync(
    Elements elements,
    TEntity entity,
    TInput input,
    CancellationToken ct)
  {
    var anyChanged = false;
    foreach (var el in elements.List)
    {
      if (el.JoinSyncer == null || string.IsNullOrEmpty(el.Path))
      {
        continue;
      }

      var raw = PropertyAccessorCache.GetValue(input, el.Path) ?? PropertyAccessorCache.GetValue(entity, el.Path);
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

      if (!ReferenceEquals(entity, input))
      {
        var inputProp = PropertyAccessorCache.GetProperty(typeof(TInput), el.Path);
        if (inputProp != null && inputProp.CanWrite && inputProp.PropertyType.IsAssignableFrom(typeof(List<long>)))
        {
          inputProp.SetValue(input, new List<long>(desired));
        }
      }
    }

    if (anyChanged)
    {
      await db.SaveChangesAsync(ct);
    }
  }
}
