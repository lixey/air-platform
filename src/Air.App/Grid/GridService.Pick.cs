using System.Runtime.CompilerServices;
using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedAsync(request, isAttach: true, handler, cancellationToken, actionName);

  protected Task<GridActionDto> AttachPickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
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
    [CallerMemberName] string? actionName = null)
    where TPicked : class, IEntity =>
    ExecutePickedAsync(request, isAttach: false, handler, cancellationToken, actionName);

  protected Task<GridActionDto> ReplacePickedAsync<TPicked>(
    GridPickRequest request,
    Action<TPicked, TEntity> handler,
    CancellationToken cancellationToken = default,
    [CallerMemberName] string? actionName = null)
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
    [CallerMemberName] string? actionName = null) =>
    ExecutePickedCoreAsync(
      request,
      actionName,
      (_, _, ct) =>
      {
        var target = InitializeAttachedEntity(factory(), GetParent());
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
    [CallerMemberName] string? actionName = null) =>
    ExecutePickedCoreAsync(
      request,
      actionName,
      async (_, _, ct) =>
      {
        var (target, refusal) = await LoadPickedReplaceTargetAsync(request, ct);
        return (target, refusal, [request.PickedId]);
      },
      (ctx, _, ct) => handler(ctx, ct),
      cancellationToken);

  private async Task<(bool IsValid, object? PickedEntity)> ResolvePickedRecordAsync(
    long pickedId,
    string? actionName,
    CancellationToken ct)
  {
    if (pickedId <= 0 || string.IsNullOrWhiteSpace(actionName))
    {
      return (false, null);
    }

    var nav = await GetNavAsync(ct: ct);
    var cols = await CreateColumnsAsync(ct);
    var kebabAction = ServicePathResolver.ToKebabCase(actionName);

    var matchedNavPicker = nav.Pickers.FirstOrDefault(p =>
      string.Equals(p.Method, actionName, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(ServicePathResolver.ToKebabCase(p.Method ?? string.Empty), kebabAction, StringComparison.OrdinalIgnoreCase));

    var pickerServicePath = matchedNavPicker?.ReferenceService;
    var readsParent = matchedNavPicker?.ReadsParent ?? false;

    if (string.IsNullOrEmpty(pickerServicePath))
    {
      var matchedCellPicker = cols.List
        .Select(c => c.Cell)
        .OfType<GridCellPickerModel>()
        .FirstOrDefault(p =>
          string.Equals(p.Method, actionName, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(p.Method, kebabAction, StringComparison.OrdinalIgnoreCase));

      if (matchedCellPicker != null)
      {
        pickerServicePath = matchedCellPicker.ReferenceService;
        readsParent = matchedCellPicker.ReadsParent;
      }
    }

    if (string.IsNullOrEmpty(pickerServicePath))
    {
      return (false, null);
    }

    var fromCatalog = Catalog.FindServiceType(pickerServicePath);
    var pickerType = fromCatalog ?? ModuleCatalog.Instance.FindServiceType(pickerServicePath);
    var catalogForInstance = fromCatalog != null ? Catalog : ModuleCatalog.Instance;
    if (pickerType == null ||
        catalogForInstance.CreateServiceInstance(pickerType, db, PlatformApp) is not IGridService pickerGrid)
    {
      return (false, null);
    }

    if (!readsParent)
    {
      readsParent = ParentScopeValidator.DeclaresParentScope(pickerType);
    }

    var picked = await pickerGrid.LoadPickedEntityAsync(
      new GridRequest
      {
        ParentService = readsParent ? ParentService : null,
        ParentId = readsParent ? ParentId : null,
        IsReferencePicker = true
      },
      pickedId,
      ct);

    return (picked != null, picked);
  }

  private Task<GridActionDto> ExecutePickedAsync<TPicked>(
    GridPickRequest request,
    bool isAttach,
    Func<GridBatchContext<TEntity>, TPicked, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken,
    string? actionName = null)
    where TPicked : class, IEntity
  {
    TPicked? loadedPicked = null;
    return ExecutePickedCoreAsync(
      request,
      actionName,
      async (parent, resolvedPicked, ct) =>
      {
        loadedPicked = resolvedPicked as TPicked
          ?? await db.Set<TPicked>().FirstOrDefaultAsync(e => e.Id == request.PickedId, ct);
        if (loadedPicked == null)
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
        await handler(ctx, loadedPicked!, target, ct);
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
  }

  private TEntity InitializeAttachedEntity(TEntity target, GridParent<TEntity> parent)
  {
    if (target is IHasTimestamps timestamps && timestamps.CreatedAt == default)
    {
      timestamps.CreatedAt = Time.GetUtcNow();
    }

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

  private GridActionDto RefusedFormNotFound() =>
    new()
    {
      Outcome = MutationOutcome.Refused,
      Messages = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
    };

  private async Task<GridActionDto> ExecutePickedCoreAsync(
    GridPickRequest request,
    string? actionName,
    Func<GridParent<TEntity>, object?, CancellationToken, Task<(TEntity? Target, GridActionDto? Refusal, IReadOnlyList<long> ContextIds)>> prepareTarget,
    Func<GridBatchContext<TEntity>, TEntity, CancellationToken, Task> runHandler,
    CancellationToken cancellationToken)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    var servicePath = ServicePathResolver.Resolve(GetType());
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
        if (!await EnsureParentScopeAsync(lockParent: true, mutationCt) || IsReadOnly)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var (isValidPicked, pickedEntity) = await ResolvePickedRecordAsync(request.PickedId, actionName, mutationCt);
        if (!isValidPicked)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["FormValidationReference"], Type = MessageType.Error }]
          };
        }

        var (target, refusal, contextIds) = await prepareTarget(GetParent(), pickedEntity, mutationCt);
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
            Messages = FormatErrors(ctx.Errors, [target], MessageType.Error)
          };
        }

        if (db.ChangeTracker.HasChanges())
        {
          await db.SaveChangesAsync(mutationCt);
        }

        var affected = await RecalculateBoundParentsForEntitiesAsync([target], mutationCt);
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
          Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, [target], MessageType.Warning) : null,
          Affected = affected
        };
      },
      cancellationToken,
      PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && committedTarget != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(committedTarget, db, cancellationToken);
    }

    return result;
  }
}
