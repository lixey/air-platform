using Platform.App.Screens.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridActionRunner
{
  public static (List<long> Ids, List<int>? Versions, bool VersionCountMismatch) NormalizeSelection(GridRequest request)
  {
    var rawIds = request.SelectedIds ?? (request.Id.HasValue ? [request.Id.Value] : []);
    var rawVersions = request.SelectedVersions;
    if (rawIds.Count == 0)
    {
      return ([], null, rawVersions is { Count: > 0 });
    }

    var hasVersions = rawVersions is { Count: > 0 };
    var versionCountMismatch = hasVersions && rawVersions!.Count != rawIds.Count;
    var seen = new Dictionary<long, int>();
    var ids = new List<long>(rawIds.Count);
    var versions = hasVersions && !versionCountMismatch ? new List<int>(rawIds.Count) : null;

    for (var i = 0; i < rawIds.Count; i++)
    {
      var id = rawIds[i];
      var ver = versions != null ? rawVersions![i] : 0;
      if (seen.TryAdd(id, ver))
      {
        ids.Add(id);
        versions?.Add(ver);
      }
      else if (versions != null && seen[id] != ver)
      {
        versionCountMismatch = true;
      }
    }

    return (ids, versions, versionCountMismatch);
  }

  public static async Task<object?> GetActionInputAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    string actionKey,
    GridRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (!await grid.BindParentAsync(request.ParentService, request.ParentId, ct))
    {
      return null;
    }

    var normalizedIds = NormalizeSelection(request).Ids;
    if (normalizedIds.Count > 0)
    {
      var reachableSet = (await grid.RestrictedQueryInternal()
        .Where(e => normalizedIds.Contains(e.Id))
        .Select(e => e.Id)
        .ToListAsync(ct)).ToHashSet();
      grid.CurrentSelectedIds = normalizedIds.Where(reachableSet.Contains).ToList();
      if (grid.CurrentSelectedIds.Count != normalizedIds.Count)
      {
        return null;
      }
    }
    else
    {
      grid.CurrentSelectedIds = [];
    }

    var isParentReadOnly = grid.ResolveParentChildBuilder()?.IsReadOnly == true;
    var actions = new GridService<TEntity, TRow>.Actions();
    await grid.InvokeSetActionsAsync(actions, ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    if (entry != null)
    {
      if (!entry.Permission.IsPermitted(grid.Feature, grid.User, entry.Handle, grid.IsGridReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly) ||
          entry.NeedsSelection && grid.CurrentSelectedIds.Count == 0 ||
          entry.IsSingle && grid.CurrentSelectedIds.Count != 1)
      {
        return null;
      }

      var defaultProvider = entry.DefaultProvider != null
        ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(grid.CurrentSelectedIds, token))
        : null;
      return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(defaultProvider, entry.Ask, grid.DbContext, grid.PlatformApp, ct);
    }

    if (isParentReadOnly)
    {
      return null;
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, grid.DbContext, grid.PlatformApp) is IFormService form)
      {
        if (grid.PlatformApp != null)
        {
          form.AttachPlatformApp(grid.PlatformApp);
        }

        if (await form.HasActionAsync(actionKey, ct))
        {
          var formReq = new FormRequest
          {
            Id = grid.CurrentSelectedIds.Count == 1 ? grid.CurrentSelectedIds[0] : request.Id,
            ParentId = grid.ParentId,
            ParentService = grid.ParentService
          };
          return await form.GetActionInputAsync(actionKey, formReq, ct);
        }
      }
    }

    return null;
  }

  public static async Task<bool> HasActionAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    string actionKey,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (string.IsNullOrWhiteSpace(actionKey))
    {
      return false;
    }

    var actions = new GridService<TEntity, TRow>.Actions();
    await grid.InvokeSetActionsAsync(actions, ct);
    if (actions.Entries.Any(e => e.Matches(actionKey)))
    {
      return true;
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, grid.DbContext, grid.PlatformApp) is IFormService form)
      {
        if (grid.PlatformApp != null)
        {
          form.AttachPlatformApp(grid.PlatformApp);
        }

        if (await form.HasActionAsync(actionKey, ct))
        {
          return true;
        }
      }
    }

    return false;
  }

  public static async Task<GridActionDto?> ExecuteDeclaredActionAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    string actionKey,
    GridRequest request,
    JsonElement? body,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    var db = grid.DbContext;

    var probeActions = new GridService<TEntity, TRow>.Actions();
    await grid.InvokeSetActionsAsync(probeActions, ct);
    var probeEntry = probeActions.Entries.FirstOrDefault(e => e.Matches(actionKey));
    if (probeEntry != null)
    {
      var (ids, versions, versionCountMismatch) = probeEntry.IgnoresSelection
        ? ([], null, false)
        : NormalizeSelection(request);

      if (probeEntry.NeedsSelection && ids.Count == 0 || probeEntry.IsSingle && ids.Count != 1)
      {
        return new GridActionDto
        {
          Outcome = MutationOutcome.Refused,
          Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      List<TEntity>? batchRecords = null;
      var servicePath = ServicePathResolver.Resolve(grid.GetType());
      var actionPath = $"{servicePath}/{probeEntry.Method}";

      var result = await MutationRunner.RunIdempotentMutationAsync<GridActionDto>(
        db,
        actionPath,
        request.IdempotencyKey,
        request.RawPayload ?? body?.GetRawText(),
        async mutationCt =>
        {
          var lockEntities = !probeEntry.IsWithoutTransaction && db.Database.CurrentTransaction != null;
          if (ids.Count > 0)
          {
            await grid.InferBoundParentIdInternalAsync(ids, mutationCt);
          }

          if (!await grid.EnsureParentScopeInternalAsync(lockParent: lockEntities, mutationCt))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          grid.CurrentSelectedIds = ids;
          var actions = new GridService<TEntity, TRow>.Actions();
          await grid.InvokeSetActionsAsync(actions, mutationCt);
          var entry = actions.Entries.FirstOrDefault(e => e.Matches(actionKey));
          if (entry?.Handler == null)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          var isParentReadOnly = grid.ResolveParentChildBuilder()?.IsReadOnly == true;
          if (!entry.Permission.IsPermitted(grid.Feature, grid.User, entry.Handle, grid.IsGridReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly) ||
              entry.NeedsSelection && ids.Count == 0 ||
              entry.IsSingle && ids.Count != 1)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          object? askInput = null;
          if (entry.Ask != null)
          {
            askInput = entry.Ask.Read(body);
            var askFailures = await entry.Ask.ValidateAsync(askInput, db, grid.PlatformApp, grid.L, mutationCt);
            if (askFailures.Count > 0)
            {
              return new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = [.. askFailures]
              };
            }
          }

          List<TEntity> records = [];
          if (ids.Count > 0)
          {
            var (loaded, missing) = await GridMutationRunner.LoadSelectedEntitiesAsync(grid, ids, mutationCt, asTracking: true, lockEntities: lockEntities);
            if (missing.Count > 0)
            {
              return new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = missing
              };
            }

            records = loaded;
          }

          if (ids.Count > 0 && GridMutationRunner.HasVersionConflict(versions, versionCountMismatch, ids, records))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Conflict,
              Messages = [new MessageDto { Detail = grid.L["FormRecordChanged"], Type = MessageType.Error }]
            };
          }

          if (entry.Offers.Count > 0 && records.Count > 0)
          {
            var recordIds = records.Select(r => r.Id).ToList();
            foreach (var offer in entry.Offers)
            {
              if (offer.Condition is Expression<Func<TEntity, bool>> cond)
              {
                var passedIds = (await db.Set<TEntity>()
                  .Where(e => recordIds.Contains(e.Id))
                  .Where(cond)
                  .Select(e => e.Id)
                  .ToListAsync(mutationCt))
                  .ToHashSet();

                foreach (var record in records)
                {
                  if (!passedIds.Contains(record.Id))
                  {
                    return new GridActionDto
                    {
                      Outcome = MutationOutcome.Refused,
                      Messages = GridMutationRunner.FormatErrors(grid, [new MutationError("grid.availability", grid.L[offer.Reason], RecordId: record.Id)], records, MessageType.Error)
                    };
                  }
                }
              }
            }
          }

          try
          {
            var ctx = new GridBatchContext<TEntity>(ids, records, token => db.SaveChangesAsync(token), db);

            foreach (var (guard, refusal, path) in entry.Guards)
            {
              var errorsBefore = ctx.Errors.Count;
              if (!await guard(ctx, records, askInput, grid.L, mutationCt))
              {
                if (ctx.Errors.Count == errorsBefore)
                {
                  ctx.AddError("grid.guard", grid.L[refusal], records.Count == 1 ? records[0].Id : null, path);
                }

                return new GridActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Messages = GridMutationRunner.FormatErrors(grid, ctx.Errors, records, MessageType.Error)
                };
              }

              if (ctx.Errors.Count > errorsBefore)
              {
                return new GridActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Messages = GridMutationRunner.FormatErrors(grid, ctx.Errors, records, MessageType.Error)
                };
              }
            }

            await entry.Handler(ctx, askInput, mutationCt);
            if (ctx.Errors.Count > 0)
            {
              return new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = GridMutationRunner.FormatErrors(grid, ctx.Errors, records, MessageType.Error)
              };
            }

            if (db.ChangeTracker.HasChanges())
            {
              await db.SaveChangesAsync(mutationCt);
            }

            var affected = !entry.IsWithoutTransaction
              ? await GridMutationRunner.RecalculateBoundParentsForEntitiesAsync(grid, records, mutationCt)
              : null;
            batchRecords = records;

            return new GridActionDto
            {
              Outcome = MutationOutcome.Committed,
              Id = records.Count == 1 ? records[0].Id : null,
              Count = records.Count,
              OpenScreen = ctx.OpenScreen,
              OpenId = ctx.OpenId,
              Title = ctx.NoticeTitle,
              Messages = ctx.NoticeMessages,
              Warnings = ctx.Warnings.Count > 0 ? GridMutationRunner.FormatErrors(grid, ctx.Warnings, records, MessageType.Warning) : null,
              Affected = affected
            };
          }
          catch (DbUpdateConcurrencyException)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Conflict,
              Messages = [new MessageDto { Detail = grid.L["FormRecordChanged"], Type = MessageType.Error }]
            };
          }
        },
        ct,
        grid.PlatformApp?.Services,
        transactional: !probeEntry.IsWithoutTransaction);

      if (result.Outcome == MutationOutcome.Committed && grid.PlatformApp != null && batchRecords is { Count: > 0 })
      {
        await grid.PlatformApp.ClearAuthCacheForEntitiesAsync(batchRecords, db, ct);
      }

      return result;
    }

    foreach (var formType in probeActions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, grid.PlatformApp) is IFormService form)
      {
        if (grid.PlatformApp != null)
        {
          form.AttachPlatformApp(grid.PlatformApp);
        }

        if (await form.HasActionAsync(actionKey, ct))
        {
          var (ids, versions, versionCountMismatch) = NormalizeSelection(request);
          if (ids.Count != 1)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          if (versionCountMismatch)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Conflict,
              Messages = [new MessageDto { Detail = grid.L["FormRecordChanged"], Type = MessageType.Error }]
            };
          }

          await grid.InferBoundParentIdInternalAsync(ids, ct);
          if (!await grid.EnsureParentScopeInternalAsync(lockParent: false, ct))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          var isParentReadOnly = grid.ResolveParentChildBuilder()?.IsReadOnly == true;
          if (!await form.IsActionPermittedForGridAsync(actionKey, grid.IsGridReadOnly, isParentReadOnly, ct))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          var targetRow = await grid.RestrictedQueryInternal().AsNoTracking().FirstOrDefaultAsync(e => e.Id == ids[0], ct);
          if (targetRow == null)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages =
              [
                new MessageDto
                {
                  Code = "grid.delete.unavailable",
                  Detail = grid.L["GridDeleteUnavailable"],
                  RecordId = ids[0],
                  Type = MessageType.Error
                }
              ]
            };
          }

          var resolvedVer = versions is { Count: 1 }
            ? versions[0]
            : (targetRow as IHasVersion)?.Ver;

          var formReq = new FormRequest
          {
            Id = ids[0],
            Ver = resolvedVer,
            ParentId = grid.ParentId,
            ParentService = grid.ParentService ?? grid.GetParentDeclaration().BoundParentService,
            IdempotencyKey = request.IdempotencyKey,
            RawPayload = request.RawPayload ?? body?.GetRawText()
          };

          var formRes = await form.ExecuteDeclaredActionAsync(actionKey, formReq, body, ct);
          if (formRes == null)
          {
            return null;
          }

          return new GridActionDto
          {
            Outcome = formRes.Outcome,
            Id = ids[0],
            Count = formRes.Outcome == MutationOutcome.Committed ? 1 : 0,
            OpenScreen = formRes.OpenScreen,
            OpenId = formRes.OpenId,
            Title = formRes.Title,
            Messages = formRes.Messages,
            Warnings = formRes.Warnings,
            Affected = formRes.Affected
          };
        }
      }
    }

    return null;
  }

  public static async Task<GridActionDto> MoveStatusAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridRequest request,
    string transitionName,
    JsonElement? body,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    var db = grid.DbContext;
    List<TEntity>? movedRecords = null;
    var servicePath = ServicePathResolver.Resolve(grid.GetType());
    var actionPath = $"{servicePath}/move-status/{transitionName.ToLowerInvariant()}";

    var result = await MutationRunner.RunIdempotentMutationAsync<GridActionDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      request.RawPayload ?? body?.GetRawText(),
      async mutationCt =>
      {
        var (selectedIds, versions, versionCountMismatch) = NormalizeSelection(request);
        await grid.InferBoundParentIdInternalAsync(selectedIds, mutationCt);
        if (!await grid.EnsureParentScopeInternalAsync(lockParent: true, mutationCt))
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var cols = grid.CreateColumnsDeclaration();
        if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = "No status flow configured for this grid.", Type = MessageType.Error }] };
        }

        if (grid.IsGridReadOnly && !cols.StatusFlowAllowsReadOnly)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (grid.PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
        {
          flowSvc.AttachPlatformApp(grid.PlatformApp);
        }

        var transition = statusFlow.Transitions.FirstOrDefault(t => string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));
        if (transition is null)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = $"Unknown status transition '{transitionName}'.", Type = MessageType.Error }] };
        }

        if (!StatusMoveNavBuilder.CanMove(grid.Feature, grid.User, transition.Name))
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = grid.L["MutationRefused"], Type = MessageType.Error }] };
        }

        if (selectedIds.Count == 0)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = "No records selected.", Type = MessageType.Error }] };
        }

        var input = transition.Ask?.Read(body);
        if (transition.Ask != null)
        {
          var askFailures = await transition.Ask.ValidateAsync(input, db, grid.PlatformApp, grid.L, mutationCt);
          if (askFailures.Count > 0)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [.. askFailures]
            };
          }
        }

        var candidates = await grid.RestrictedQueryInternal()
          .AsNoTracking()
          .Where(e => selectedIds.Contains(e.Id))
          .OrderBy(e => e.Id)
          .ToListAsync(mutationCt);
        if (candidates.Count != selectedIds.Count)
        {
          var foundIds = candidates.Select(c => c.Id).ToHashSet();
          var missingCandidates = selectedIds
            .Where(id => !foundIds.Contains(id))
            .Select(id => new MessageDto
            {
              Code = "grid.delete.unavailable",
              Detail = grid.L["GridDeleteUnavailable"],
              RecordId = id,
              Type = MessageType.Error
            })
            .ToList();
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = missingCandidates
          };
        }

        var context = new MutationContext(db);
        await statusFlow.LockRecordsAsync(context, candidates, mutationCt);

        var (entities, missing) = await GridMutationRunner.LoadSelectedEntitiesAsync(grid, selectedIds, mutationCt, asTracking: true, lockEntities: true);
        if (missing.Count > 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = missing
          };
        }

        if (GridMutationRunner.HasVersionConflict(versions, versionCountMismatch, selectedIds, entities))
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = grid.L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }

        try
        {
          await statusFlow.MoveAsync(context, entities, transition.Name, input, mutationCt);

          if (context.Errors.Count > 0)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = GridMutationRunner.FormatErrors(grid, context.Errors, entities, MessageType.Error)
            };
          }

          var affected = await GridMutationRunner.RecalculateBoundParentsForEntitiesAsync(grid, entities, mutationCt);
          movedRecords = entities;

          return new GridActionDto
          {
            Outcome = MutationOutcome.Committed,
            Id = entities.Count == 1 ? entities[0].Id : null,
            Count = entities.Count,
            Warnings = context.Warnings.Count > 0 ? GridMutationRunner.FormatErrors(grid, context.Warnings, entities, MessageType.Warning) : null,
            Affected = affected
          };
        }
        catch (DbUpdateConcurrencyException)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = grid.L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      },
      ct,
      grid.PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && grid.PlatformApp != null && movedRecords is { Count: > 0 })
    {
      await grid.PlatformApp.ClearAuthCacheForEntitiesAsync(movedRecords, db, ct);
    }

    return result;
  }
}
