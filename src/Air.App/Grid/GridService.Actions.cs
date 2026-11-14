using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  private static (List<long> Ids, List<int>? Versions, bool VersionCountMismatch) NormalizeSelection(GridRequest request)
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

  public virtual async Task<object?> GetActionInputAsync(string actionKey, GridRequest request, CancellationToken ct = default)
  {
    if (!await BindParentAsync(request.ParentService, request.ParentId, ct))
    {
      return null;
    }

    var normalizedIds = NormalizeSelection(request).Ids;
    if (normalizedIds.Count > 0)
    {
      var reachableSet = (await RestrictedQuery()
        .Where(e => normalizedIds.Contains(e.Id))
        .Select(e => e.Id)
        .ToListAsync(ct)).ToHashSet();
      SelectedIds = normalizedIds.Where(reachableSet.Contains).ToList();
      if (SelectedIds.Count != normalizedIds.Count)
      {
        return null;
      }
    }
    else
    {
      SelectedIds = [];
    }

    var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
    var actions = await BuildActionsAsync(ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    if (entry != null)
    {
      if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly) ||
          entry.NeedsSelection && SelectedIds.Count == 0 ||
          entry.IsSingle && SelectedIds.Count != 1)
      {
        return null;
      }

      var defaultProvider = entry.DefaultProvider != null
        ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(SelectedIds, token))
        : null;
      return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(defaultProvider, entry.Ask, db, PlatformApp, ct);
    }

    if (isParentReadOnly)
    {
      return null;
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form)
      {
        if (await form.HasActionAsync(actionKey, ct))
        {
          var formReq = new FormRequest
          {
            Id = SelectedIds.Count == 1 ? SelectedIds[0] : request.Id,
            ParentId = ParentId,
            ParentService = ParentService
          };
          return await form.GetActionInputAsync(actionKey, formReq, ct);
        }
      }
    }

    return null;
  }

  public virtual async Task<bool> HasActionAsync(string actionKey, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(actionKey))
    {
      return false;
    }

    var actions = await BuildActionsAsync(ct);
    if (actions.Entries.Any(e => e.Matches(actionKey)))
    {
      return true;
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form)
      {
        if (await form.HasActionAsync(actionKey, ct))
        {
          return true;
        }
      }
    }

    return false;
  }

  public virtual async Task<GridActionDto?> ExecuteDeclaredActionAsync(
    string actionKey,
    GridRequest request,
    JsonElement? body,
    CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;

    var probeActions = await BuildActionsAsync(ct);
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
          Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
        };
      }

      List<TEntity>? batchRecords = null;
      var servicePath = ServicePathResolver.Resolve(GetType());
      var actionPath = $"{servicePath}/{probeEntry.Method}";

      var result = await MutationRunner.RunIdempotentMutationAsync<GridActionDto>(
        db,
        actionPath,
        request.IdempotencyKey,
        ResolveIdempotentPayload(request.IdempotencyKey, request.RawPayload, body),
        async mutationCt =>
        {
          var lockEntities = !probeEntry.IsWithoutTransaction && db.Database.CurrentTransaction != null;
          if (ids.Count > 0)
          {
            await InferBoundParentIdFromIdsAsync(ids, mutationCt);
          }

          if (!await EnsureParentScopeAsync(lockParent: lockEntities, mutationCt))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          SelectedIds = ids;
          var actions = await BuildActionsAsync(mutationCt, bypassCache: true);
          var entry = actions.Entries.FirstOrDefault(e => e.Matches(actionKey));
          if (entry?.Handler == null)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
          if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly) ||
              entry.NeedsSelection && ids.Count == 0 ||
              entry.IsSingle && ids.Count != 1)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          object? askInput = null;
          if (entry.Ask != null)
          {
            askInput = entry.Ask.Read(body);
            var askFailures = await entry.Ask.ValidateAsync(askInput, db, PlatformApp, L, mutationCt);
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
            var (loaded, missing) = await LoadSelectedEntitiesAsync(ids, mutationCt, asTracking: true, lockEntities: lockEntities);
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

          if (ids.Count > 0 && HasVersionConflict(versions, versionCountMismatch, ids, records))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Conflict,
              Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
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
                      Messages = FormatErrors([new MutationError("grid.availability", L[offer.Reason], RecordId: record.Id)], records, MessageType.Error)
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
              if (!await guard(ctx, records, askInput, L, mutationCt))
              {
                if (ctx.Errors.Count == errorsBefore)
                {
                  ctx.AddError("grid.guard", L[refusal], records.Count == 1 ? records[0].Id : null, path);
                }

                return new GridActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Messages = FormatErrors(ctx.Errors, records, MessageType.Error)
                };
              }

              if (ctx.Errors.Count > errorsBefore)
              {
                return new GridActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Messages = FormatErrors(ctx.Errors, records, MessageType.Error)
                };
              }
            }

            await entry.Handler(ctx, askInput, mutationCt);
            if (ctx.Errors.Count > 0)
            {
              return new GridActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = FormatErrors(ctx.Errors, records, MessageType.Error)
              };
            }

            if (db.ChangeTracker.HasChanges())
            {
              await db.SaveChangesAsync(mutationCt);
            }

            var affected = !entry.IsWithoutTransaction
              ? await RecalculateBoundParentsForEntitiesAsync(records, mutationCt)
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
              Warnings = ctx.Warnings.Count > 0 ? FormatErrors(ctx.Warnings, records, MessageType.Warning) : null,
              Affected = affected
            };
          }
          catch (DbUpdateConcurrencyException)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Conflict,
              Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
            };
          }
        },
        ct,
        PlatformApp?.Services,
        transactional: !probeEntry.IsWithoutTransaction);

      if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && batchRecords is { Count: > 0 })
      {
        await PlatformApp.ClearAuthCacheForEntitiesAsync(batchRecords, db, ct);
      }

      return result;
    }

    foreach (var formType in probeActions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form)
      {
        if (await form.HasActionAsync(actionKey, ct))
        {
          var (ids, versions, versionCountMismatch) = NormalizeSelection(request);
          if (ids.Count != 1)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          if (versionCountMismatch)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Conflict,
              Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
            };
          }

          await InferBoundParentIdFromIdsAsync(ids, ct);
          if (!await EnsureParentScopeAsync(lockParent: false, ct))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
          if (!await form.IsActionPermittedForGridAsync(actionKey, IsReadOnly, isParentReadOnly, ct))
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
            };
          }

          var targetRow = await RestrictedQuery().AsNoTracking().FirstOrDefaultAsync(e => e.Id == ids[0], ct);
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
                  Detail = L["GridDeleteUnavailable"],
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
            ParentId = ParentId,
            ParentService = ParentService ?? GetParent().BoundParentService,
            IdempotencyKey = request.IdempotencyKey,
            RawPayload = ResolveIdempotentPayload(request.IdempotencyKey, request.RawPayload, body)
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

  public virtual async Task<GridActionDto> MoveStatusAsync(
    GridRequest request,
    string transitionName,
    JsonElement? body,
    CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    List<TEntity>? movedRecords = null;
    var servicePath = ServicePathResolver.Resolve(GetType());
    var actionPath = $"{servicePath}/{StatusMoveNavBuilder.BuildMoveStatusMethod(transitionName)}";

    var result = await MutationRunner.RunIdempotentMutationAsync<GridActionDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      ResolveIdempotentPayload(request.IdempotencyKey, request.RawPayload, body),
      async mutationCt =>
      {
        var (selectedIds, versions, versionCountMismatch) = NormalizeSelection(request);
        await InferBoundParentIdFromIdsAsync(selectedIds, mutationCt);
        if (!await EnsureParentScopeAsync(lockParent: true, mutationCt))
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var cols = await CreateColumnsAsync(mutationCt);
        if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = L["StatusFlowNotConfigured"], Type = MessageType.Error }] };
        }

        (statusFlow as IPlatformService)?.BindContext(PlatformApp);

        var transition = statusFlow.AllTransitions.FirstOrDefault(t => StatusMoveNavBuilder.MatchesTransition(t.Name, transitionName));
        if (transition is null)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = L["StatusTransitionUnknown", transitionName], Type = MessageType.Error }] };
        }

        if (!StatusMoveNavBuilder.CanMoveOnScreen(
              Feature,
              User,
              transition,
              IsServiceLockedReadOnly,
              cols.StatusFlowAllowsReadOnly,
              ResolveParentChildBuilder()?.IsReadOnly == true,
              typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity))))
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }] };
        }

        if (selectedIds.Count == 0)
        {
          return new GridActionDto { Outcome = MutationOutcome.Refused, Messages = [new MessageDto { Detail = L["NoRecordsSelected"], Type = MessageType.Error }] };
        }

        var input = transition.Ask?.Read(body);
        if (transition.Ask != null)
        {
          var askFailures = await transition.Ask.ValidateAsync(input, db, PlatformApp, L, mutationCt);
          if (askFailures.Count > 0)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [.. askFailures]
            };
          }
        }

        var context = new MutationContext(db);
        List<TEntity> entities;
        List<MessageDto> missing;

        if (statusFlow.HasCustomRecordLock)
        {
          var (candidates, missingCandidates) = await LoadSelectedEntitiesAsync(
            selectedIds,
            mutationCt,
            asTracking: false,
            lockEntities: false);
          if (missingCandidates.Count > 0)
          {
            return new GridActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = missingCandidates
            };
          }

          await statusFlow.LockRecordsAsync(context, candidates, mutationCt);
          (entities, missing) = await LoadSelectedEntitiesAsync(
            selectedIds,
            mutationCt,
            asTracking: true,
            lockEntities: true,
            reloadTracked: true);
        }
        else
        {
          (entities, missing) = await LoadSelectedEntitiesAsync(
            selectedIds,
            mutationCt,
            asTracking: true,
            lockEntities: true);
          if (missing.Count == 0)
          {
            statusFlow.MarkRecordsLocked(context, entities);
          }
        }

        if (missing.Count > 0)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = missing
          };
        }

        if (HasVersionConflict(versions, versionCountMismatch, selectedIds, entities))
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
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
              Messages = FormatErrors(context.Errors, entities, MessageType.Error)
            };
          }

          var affected = await RecalculateBoundParentsForEntitiesAsync(entities, mutationCt);
          movedRecords = entities;

          return new GridActionDto
          {
            Outcome = MutationOutcome.Committed,
            Id = entities.Count == 1 ? entities[0].Id : null,
            Count = entities.Count,
            Warnings = context.Warnings.Count > 0 ? FormatErrors(context.Warnings, entities, MessageType.Warning) : null,
            Affected = affected
          };
        }
        catch (DbUpdateConcurrencyException)
        {
          return new GridActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      },
      ct,
      PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && movedRecords is { Count: > 0 })
    {
      await PlatformApp.ClearAuthCacheForEntitiesAsync(movedRecords, db, ct);
    }

    return result;
  }

  private static string? ResolveIdempotentPayload(string? idempotencyKey, string? rawPayload, JsonElement? body) =>
    !string.IsNullOrWhiteSpace(idempotencyKey) ? rawPayload ?? body?.GetRawText() : null;
}
