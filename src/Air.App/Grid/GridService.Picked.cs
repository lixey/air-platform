namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  private async Task<bool> ValidatePickedRecordAsync(long pickedId, string? actionName, CancellationToken ct)
  {
    if (pickedId <= 0)
    {
      return false;
    }

    var nav = GetNav();
    var cols = CreateColumns();
    var kebabAction = !string.IsNullOrEmpty(actionName) ? ServicePathResolver.ToKebabCase(actionName) : null;

    var matchedNavPicker = nav.Pickers
      .FirstOrDefault(p =>
        !string.IsNullOrEmpty(actionName) &&
        (string.Equals(p.Method, actionName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(ServicePathResolver.ToKebabCase(p.Method ?? ""), kebabAction, StringComparison.OrdinalIgnoreCase)));

    var pickerServicePath = matchedNavPicker?.ReferenceService;
    var readsParent = matchedNavPicker?.ReadsParent ?? false;

    pickerServicePath ??= cols.List
      .Select(c => c.Cell)
      .OfType<GridCellPickerModel>()
      .FirstOrDefault(p =>
        !string.IsNullOrEmpty(actionName) &&
        (string.Equals(p.Method, actionName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(p.Method, kebabAction, StringComparison.OrdinalIgnoreCase)))
      ?.ReferenceService;

    if (string.IsNullOrEmpty(pickerServicePath))
    {
      var allPickerServices = nav.Pickers.Select(p => p.ReferenceService)
        .Concat(cols.List.Select(c => c.Cell).OfType<GridCellPickerModel>().Select(p => p.ReferenceService))
        .Where(s => !string.IsNullOrEmpty(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
      if (allPickerServices.Count == 1)
      {
        pickerServicePath = allPickerServices[0];
        readsParent = nav.Pickers
          .FirstOrDefault(p => string.Equals(p.ReferenceService, pickerServicePath, StringComparison.OrdinalIgnoreCase))
          ?.ReadsParent ?? false;
      }
    }

    if (string.IsNullOrEmpty(pickerServicePath))
    {
      return true;
    }

    var pickerType = ModuleCatalog.Instance.FindServiceType(pickerServicePath);
    if (pickerType == null ||
        ModuleCatalog.Instance.CreateServiceInstance(pickerType, db, PlatformApp) is not IGridService pickerGrid)
    {
      return false;
    }

    var countDto = await pickerGrid.GetCountAsync(new GridRequest
    {
      ParentService = readsParent ? ParentService : null,
      ParentId = readsParent ? ParentId : null,
      IsReferencePicker = true,
      Filter = Platform.Data.Contracts.FilterNode.Rule("id", Platform.Data.Contracts.FilterOperator.Equals, pickedId)
    }, ct);

    return countDto.Total > 0;
  }

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedAsync(request, isAttach: true, handler, cancellationToken, actionName);

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedAsync<TPicked>(
      request,
      isAttach: true,
      (_, picked, target, _) =>
      {
        handler(picked, target);
        return Task.CompletedTask;
      },
      cancellationToken,
      actionName);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedAsync(request, isAttach: false, handler, cancellationToken, actionName);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedAsync<TPicked>(
      request,
      isAttach: false,
      (_, picked, target, _) =>
      {
        handler(picked, target);
        return Task.CompletedTask;
      },
      cancellationToken,
      actionName);

  protected async Task<GridActionDto> AttachPickedAsync(
    GridPickRequest request,
    Action<TEntity, long> bindPicked,
    Func<TEntity> factory,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> afterAttach,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      if (!await ValidatePickedRecordAsync(request.PickedId, actionName, mutationCt))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormValidationReference"], Type = MessageType.Error }]
        };
      }

      var parent = GetParent();
      var target = factory();
      if (target is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
      {
        tenantEntity.TenantId = tid;
      }

      if (parent.BoundProperty != null && ParentId is > 0 && parent.BoundProperty.CanWrite)
      {
        parent.BoundProperty.SetValue(target, ParentId.Value);
      }

      bindPicked(target, request.PickedId);
      db.Set<TEntity>().Add(target);

      var ctx = new GridBatchContext<TEntity>([request.PickedId], [target], ct => db.SaveChangesAsync(ct), db);
      await afterAttach(ctx, mutationCt);
      if (ctx.Errors.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      if (PlatformApp != null)
      {
        await PlatformApp.ClearAuthCacheForEntityAsync(target, db, mutationCt);
      }

      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = target.Id,
        Count = 1,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null
      };
    }, ct: cancellationToken);
  }

  protected async Task<GridActionDto> ReplacePickedAsync(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      if (!await ValidatePickedRecordAsync(request.PickedId, actionName, mutationCt))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormValidationReference"], Type = MessageType.Error }]
        };
      }

      var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault() ?? 0;
      if (targetId <= 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
        };
      }

      var (entities, missing) = await LoadSelectedEntitiesAsync([targetId], mutationCt, asTracking: true, lockEntities: true);
      if (missing.Count > 0 || entities.Count == 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
        };
      }

      var target = entities[0];
      var ctx = new GridBatchContext<TEntity>([request.PickedId], [target], ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, mutationCt);
      if (ctx.Errors.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      if (PlatformApp != null)
      {
        await PlatformApp.ClearAuthCacheForEntityAsync(target, db, mutationCt);
      }

      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = target.Id,
        Count = 1,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null
      };
    }, ct: cancellationToken);
  }

  private async Task<GridActionDto> ExecutePickedAsync<TPicked>(
    GridPickRequest request,
    bool isAttach,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken,
    string? actionName = null)
    where TPicked : class, IEntity
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    return await db.RunMutationAsync(async mutationCt =>
    {
      if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      if (!await ValidatePickedRecordAsync(request.PickedId, actionName, mutationCt))
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormValidationReference"], Type = MessageType.Error }]
        };
      }

      var picked = await db.Set<TPicked>().FirstOrDefaultAsync(e => e.Id == request.PickedId, mutationCt);
      if (picked == null)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
        };
      }

      var parent = GetParent();

      TEntity target;
      if (isAttach)
      {
        target = new TEntity();
        if (target is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
        {
          tenantEntity.TenantId = tid;
        }

        if (parent.BoundProperty != null && ParentId is > 0 && parent.BoundProperty.CanWrite)
        {
          parent.BoundProperty.SetValue(target, ParentId.Value);
        }
      }
      else
      {
        var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault() ?? 0;
        if (targetId <= 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
          };
        }

        var (loadedList, missing) = await LoadSelectedEntitiesAsync([targetId], mutationCt, asTracking: true, lockEntities: true);
        if (missing.Count > 0 || loadedList.Count == 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
          };
        }

        target = loadedList[0];
      }

      var ctx = new GridBatchContext<TEntity>(target.Id > 0 ? [target.Id] : [], [target], ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, picked, target, mutationCt);
      if (ctx.Errors.Count > 0)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (isAttach)
      {
        db.Set<TEntity>().Add(target);
      }
      else if (db.Entry(target).State == EntityState.Detached)
      {
        db.Set<TEntity>().Update(target);
      }

      await db.SaveChangesAsync(mutationCt);
      if (PlatformApp != null)
      {
        await PlatformApp.ClearAuthCacheForEntityAsync(target, db, mutationCt);
      }

      return new GridActionDto
      {
        Outcome = MutationOutcome.Committed,
        Id = target.Id,
        Count = 1,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null
      };
    }, ct: cancellationToken);
  }
}
