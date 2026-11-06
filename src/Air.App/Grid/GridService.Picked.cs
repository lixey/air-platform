using Platform.App.Internals;

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
        ModuleCatalog.Instance.CreateServiceInstance(pickerType, db, PlatformApp) is not IGridService pickerGrid)
    {
      return false;
    }

    if (!readsParent)
    {
      readsParent = ParentScopeValidator.DeclaresParentScope(pickerType);
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

  protected Task<GridActionDto> AttachPickedAsync(
    GridPickRequest request,
    Action<TEntity, long> bindPicked,
    Func<TEntity> factory,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> afterAttach,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null) =>
    ExecutePickedCoreAsync(
      request,
      actionName,
      (parent, _) =>
      {
        var target = InitializeAttachedEntity(factory(), parent);
        bindPicked(target, request.PickedId);
        db.Set<TEntity>().Add(target);
        return Task.FromResult<(TEntity?, GridActionDto?, IReadOnlyList<long>)>((target, null, [request.PickedId]));
      },
      (ctx, _, ct) => afterAttach(ctx, ct),
      cancellationToken);

  protected Task<GridActionDto> ReplacePickedAsync(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null) =>
    ExecutePickedCoreAsync(
      request,
      actionName,
      async (_, ct) =>
      {
        var (target, refusal) = await LoadPickedReplaceTargetAsync(request, ct);
        return (target, refusal, [request.PickedId]);
      },
      (ctx, _, ct) => handler(ctx, ct),
      cancellationToken);

  private Task<GridActionDto> ExecutePickedAsync<TPicked>(
    GridPickRequest request,
    bool isAttach,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken,
    string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedCoreAsync(
      request,
      actionName,
      async (parent, ct) =>
      {
        var picked = await db.Set<TPicked>().FirstOrDefaultAsync(e => e.Id == request.PickedId, ct);
        if (picked == null)
        {
          return (null, RefusedFormNotFound(), []);
        }

        if (isAttach)
        {
          var target = InitializeAttachedEntity(new TEntity(), parent);
          return (target, null, []);
        }

        var (loaded, refusal) = await LoadPickedReplaceTargetAsync(request, ct);
        return (loaded, refusal, loaded is { Id: > 0 } ? [loaded.Id] : []);
      },
      async (ctx, target, ct) =>
      {
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

  private TEntity InitializeAttachedEntity(TEntity target, Parent parent)
  {
    if (target is IHasTenant tenantEntity && tenantEntity.TenantId == 0 && db.TenantContext.TenantId is { } tid)
    {
      tenantEntity.TenantId = tid;
    }

    if (parent.BoundProperty != null && ParentId is > 0 && parent.BoundProperty.CanWrite)
    {
      parent.BoundProperty.SetValue(target, ParentId.Value);
    }

    return target;
  }

  private async Task<(TEntity? Target, GridActionDto? Refusal)> LoadPickedReplaceTargetAsync(
    GridPickRequest request,
    CancellationToken ct)
  {
    var targetId = request.Id ?? request.SelectedIds?.FirstOrDefault() ?? 0;
    if (targetId <= 0)
    {
      return (null, RefusedFormNotFound());
    }

    var (entities, missing) = await LoadSelectedEntitiesAsync([targetId], ct, asTracking: true, lockEntities: true);
    return missing.Count > 0 || entities.Count == 0
      ? (null, RefusedFormNotFound())
      : (entities[0], null);
  }

  private GridActionDto RefusedFormNotFound() => new()
  {
    Outcome = MutationOutcome.Refused,
    Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
  };

  private async Task<GridActionDto> ExecutePickedCoreAsync(
    GridPickRequest request,
    string? actionName,
    Func<Parent, CancellationToken, Task<(TEntity? Target, GridActionDto? Refusal, IReadOnlyList<long> ContextIds)>> prepareTarget,
    Func<GridBatchContext<TEntity>, TEntity, CancellationToken, Task> runHandler,
    CancellationToken cancellationToken)
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

      var (target, refusal, contextIds) = await prepareTarget(GetParent(), mutationCt);
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
          Warnings = FormatErrors(ctx.Errors, [target], MessageType.Error)
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      await RecalculateBoundParentsForEntitiesAsync([target], mutationCt);

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
