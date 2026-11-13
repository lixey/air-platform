using Platform.App.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridPickExecutor
{
  public static async Task<bool> ValidatePickedRecordAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    long pickedId,
    string? actionName,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (pickedId <= 0)
    {
      return false;
    }

    var nav = grid.GetNavDeclaration();
    var cols = grid.CreateColumnsDeclaration();
    var kebabAction = !string.IsNullOrEmpty(actionName) ? ServicePathResolver.ToKebabCase(actionName) : null;

    var matchedNavPicker = nav.Pickers
      .FirstOrDefault(p =>
        !string.IsNullOrEmpty(actionName) &&
        (string.Equals(p.Method, actionName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(ServicePathResolver.ToKebabCase(p.Method ?? ""), kebabAction, StringComparison.OrdinalIgnoreCase)));

    var pickerServicePath = matchedNavPicker?.ReferenceService;
    var readsParent = matchedNavPicker?.ReadsParent ?? false;

    var matchedCellPicker = cols.List
      .Select(c => c.Cell)
      .OfType<GridCellPickerModel>()
      .FirstOrDefault(p =>
        !string.IsNullOrEmpty(actionName) &&
        (string.Equals(p.Method, actionName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(p.Method, kebabAction, StringComparison.OrdinalIgnoreCase)));

    if (string.IsNullOrEmpty(pickerServicePath) && matchedCellPicker != null)
    {
      pickerServicePath = matchedCellPicker.ReferenceService;
      readsParent = matchedCellPicker.ReadsParent;
    }

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
          ?.ReadsParent
          ?? cols.List
            .Select(c => c.Cell)
            .OfType<GridCellPickerModel>()
            .FirstOrDefault(p => string.Equals(p.ReferenceService, pickerServicePath, StringComparison.OrdinalIgnoreCase))
            ?.ReadsParent
          ?? false;
      }
    }

    if (string.IsNullOrEmpty(pickerServicePath))
    {
      return true;
    }

    var pickerType = ModuleCatalog.Instance.FindServiceType(pickerServicePath);
    if (pickerType == null ||
        ModuleCatalog.Instance.CreateServiceInstance(pickerType, grid.DbContext, grid.PlatformApp) is not IGridService pickerGrid)
    {
      return false;
    }

    if (!readsParent)
    {
      readsParent = ParentScopeValidator.DeclaresParentScope(pickerType);
    }

    var countDto = await pickerGrid.GetCountAsync(new GridRequest
    {
      ParentService = readsParent ? grid.ParentService : null,
      ParentId = readsParent ? grid.ParentId : null,
      IsReferencePicker = true,
      Filter = FilterNode.Rule("id", FilterOperator.Equals, pickedId)
    }, ct);

    return countDto.Total > 0;
  }

  public static Task<GridActionDto> ExecutePickedAsync<TEntity, TRow, TPicked>(
    GridService<TEntity, TRow> grid,
    GridPickRequest request,
    bool isAttach,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken,
    string? actionName = null)
    where TEntity : class, IEntity, new()
    where TRow : class
    where TPicked : class, IEntity =>
    ExecutePickedCoreAsync(
      grid,
      request,
      actionName,
      async (parent, ct) =>
      {
        var picked = await grid.DbContext.Set<TPicked>().FirstOrDefaultAsync(e => e.Id == request.PickedId, ct);
        if (picked == null)
        {
          return (null, RefusedFormNotFound(grid), []);
        }

        if (isAttach)
        {
          var target = InitializeAttachedEntity(grid, new TEntity(), parent);
          return (target, null, []);
        }

        var (loaded, refusal) = await LoadPickedReplaceTargetAsync(grid, request, ct);
        return (loaded, refusal, loaded is { Id: > 0 } ? [loaded.Id] : []);
      },
      async (ctx, target, ct) =>
      {
        var db = grid.DbContext;
        var picked = (await db.Set<TPicked>().FirstAsync(e => e.Id == request.PickedId, ct))!;
        await handler(ctx, picked, target, ct);
        if (ctx.Errors.Count == 0)
        {
          if (isAttach)
          {
            db.Set<TEntity>().Add(target);
          }
          else if (db.Entry(target).State == EntityState.Detached)
          {
            db.Set<TEntity>().Update(target);
          }
        }
      },
      cancellationToken);

  public static TEntity InitializeAttachedEntity<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    TEntity target,
    GridService<TEntity, TRow>.Parent parent)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (target is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && grid.DbContext.TenantContext.TenantId is { } tid)
    {
      tenantEntity.TenantId = tid;
    }

    if (parent.BoundProperty != null && grid.ParentId is > 0 && parent.BoundProperty.CanWrite)
    {
      parent.BoundProperty.SetValue(target, grid.ParentId.Value);
    }

    return target;
  }

  public static async Task<(TEntity? Target, GridActionDto? Refusal)> LoadPickedReplaceTargetAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridPickRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault() ?? 0;
    if (targetId <= 0)
    {
      return (null, RefusedFormNotFound(grid));
    }

    var (entities, missing) = await GridMutationRunner.LoadSelectedEntitiesAsync(grid, [targetId], ct, asTracking: true, lockEntities: true);
    return missing.Count > 0 || entities.Count == 0
      ? (null, RefusedFormNotFound(grid))
      : (entities[0], null);
  }

  public static GridActionDto RefusedFormNotFound<TEntity, TRow>(GridService<TEntity, TRow> grid)
    where TEntity : class, IEntity, new()
    where TRow : class =>
    new()
    {
      Outcome = MutationOutcome.Refused,
      Messages = [new MessageDto { Detail = grid.L["FormNotFound"], Type = MessageType.Error }]
    };

  public static async Task<GridActionDto> ExecutePickedCoreAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridPickRequest request,
    string? actionName,
    Func<GridService<TEntity, TRow>.Parent, CancellationToken, Task<(TEntity? Target, GridActionDto? Refusal, IReadOnlyList<long> ContextIds)>> prepareTarget,
    Func<GridBatchContext<TEntity>, TEntity, CancellationToken, Task> runHandler,
    CancellationToken cancellationToken)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    var db = grid.DbContext;
    var servicePath = ServicePathResolver.Resolve(grid.GetType());
    var actionPath = $"{servicePath}/{ServicePathResolver.ToKebabCase(actionName ?? "pick")}";
    var rawPayload = request.RawPayload ?? $"{request.ParentService}|{request.ParentId}|{request.Id}|{request.PickedId}";
    TEntity? committedTarget = null;

    var result = await MutationRunner.RunIdempotentMutationAsync<GridActionDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      rawPayload,
      async mutationCt =>
      {
        if (!await grid.EnsureParentScopeInternalAsync(lockParent: true, mutationCt) || grid.IsGridReadOnly)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (!await ValidatePickedRecordAsync(grid, request.PickedId, actionName, mutationCt))
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = grid.L["FormValidationReference"], Type = MessageType.Error }]
          };
        }

        var (target, refusal, contextIds) = await prepareTarget(grid.GetParentDeclaration(), mutationCt);
        if (refusal != null || target == null)
        {
          return refusal!;
        }

        var ctx = new GridBatchContext<TEntity>(contextIds, [target], ct => db.SaveChangesAsync(ct), db);
        await runHandler(ctx, target, mutationCt);
        if (ctx.Errors.Count > 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = GridMutationRunner.FormatErrors(grid, ctx.Errors, [target], MessageType.Error)
          };
        }

        if (db.ChangeTracker.HasChanges())
        {
          await db.SaveChangesAsync(mutationCt);
        }

        var affected = await GridMutationRunner.RecalculateBoundParentsForEntitiesAsync(grid, [target], mutationCt);
        committedTarget = target;

        return new GridActionDto
        {
          Outcome = MutationOutcome.Committed,
          Id = target.Id,
          Count = 1,
          OpenScreen = ctx.OpenScreen,
          OpenId = ctx.OpenId,
          Title = ctx.NoticeTitle,
          Messages = ctx.NoticeMessages,
          Warnings = ctx.Warnings.Count > 0 ? GridMutationRunner.FormatErrors(grid, ctx.Warnings, [target], MessageType.Warning) : null,
          Affected = affected
        };
      },
      cancellationToken,
      grid.PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && grid.PlatformApp != null && committedTarget != null)
    {
      await grid.PlatformApp.ClearAuthCacheForEntityAsync(committedTarget, db, cancellationToken);
    }

    return result;
  }
}
