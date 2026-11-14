using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  public virtual async Task<bool> HasActionAsync(string actionKey, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(actionKey))
    {
      return false;
    }

    var actions = await EnsureActionsAsync(ct);
    return actions.Entries.Any(e => e.Matches(actionKey));
  }

  public virtual async Task<object?> GetActionInputAsync(string actionKey, FormRequest request, CancellationToken ct = default)
  {
    SetRequest(request);
    try
    {
      await EnsureEntityAndParentLoadedAsync(ct);
    }
    catch (KeyNotFoundException)
    {
      return null;
    }

    if (!IsNew && CurrentEntity == null)
    {
      return null;
    }

    var actions = await EnsureActionsAsync(ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    var isParentReadOnly = (await ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    if (entry == null || !entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
    {
      return null;
    }

    var defaultProvider = entry.DefaultProvider != null
      ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(CurrentEntity, token))
      : null;
    return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(defaultProvider, entry.Ask, db, PlatformApp, ct);
  }

  public virtual async Task<FormActionDto?> ExecuteDeclaredActionAsync(
    string actionKey,
    FormRequest request,
    JsonElement? body,
    CancellationToken ct = default)
  {
    SetRequest(request);
    IsNew = false;

    var probeActions = await BuildActionsAsync(ct);
    var probeEntry = probeActions.Entries.FirstOrDefault(e => e.Matches(actionKey));
    if (probeEntry == null)
    {
      return null;
    }

    if (request.Id is not > 0)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Messages = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
      };
    }

    var parent = GetParent();
    TEntity? actionEntity = null;
    var actionPath = $"{Service}/{probeEntry.Method}";

    var result = await MutationRunner.RunIdempotentMutationAsync<FormActionDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      ResolveIdempotentPayload(request.IdempotencyKey, request.RawPayload, body),
      async mutationCt =>
      {
        var entity = await LockAndLoadExistingAsync(request.Id.Value, parent, mutationCt);
        CurrentEntity = entity;

        var actions = await EnsureActionsAsync(mutationCt, reload: true);
        var entry = actions.Entries.FirstOrDefault(e => e.Matches(actionKey));
        if (entry?.Handler == null)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var isParentReadOnly = (await ResolveParentChildBuilderAsync(ct: mutationCt))?.IsReadOnly == true;
        if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (entity is IHasVersion versioned &&
            (!request.Ver.HasValue || request.Ver.Value <= 0 || versioned.Ver != request.Ver.Value))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }

        object? askInput = null;
        if (entry.Ask != null)
        {
          askInput = entry.Ask.Read(body ?? request.Value);
          var askFailures = await entry.Ask.ValidateAsync(askInput, db, PlatformApp, L, mutationCt);
          if (askFailures.Count > 0)
          {
            return new FormActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [.. askFailures]
            };
          }
        }

        if (entry.Offers.Count > 0)
        {
          foreach (var offer in entry.Offers)
          {
            if (offer.Condition is Expression<Func<TEntity, bool>> cond)
            {
              var passes = await db.Set<TEntity>().Where(e => e.Id == entity.Id).AnyAsync(cond, mutationCt);
              if (!passes)
              {
                return new FormActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Messages = [new MessageDto { Detail = L[offer.Reason], RecordId = entity.Id, Type = MessageType.Error }]
                };
              }
            }
          }
        }

        try
        {
          var oldParentId = parent.BoundProperty != null ? parent.BoundProperty.GetValue(entity) as long? : null;
          var ctx = new FormActionContext<TEntity>(entity, token => db.SaveChangesAsync(token), db);

          foreach (var (guard, refusal, path) in entry.Guards)
          {
            var errorsBefore = ctx.Errors.Count;
            if (!await guard(ctx, entity, askInput, mutationCt))
            {
              if (ctx.Errors.Count == errorsBefore)
              {
                ctx.AddError("form.guard", L[refusal], entity.Id, path);
              }

              return new FormActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
              };
            }

            if (ctx.Errors.Count > errorsBefore)
            {
              return new FormActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
              };
            }
          }

          await entry.Handler(ctx, entity, askInput, mutationCt);
          if (ctx.Errors.Count > 0)
          {
            return new FormActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
            };
          }

          if (db.ChangeTracker.HasChanges())
          {
            await db.SaveChangesAsync(mutationCt);
          }

          List<AffectedDto>? affected = null;
          if (parent.BoundProperty != null || parent.BoundParentServiceType != null)
          {
            var newParentId = parent.BoundProperty != null
              ? parent.BoundProperty.GetValue(entity) as long? ?? Request.ParentId
              : Request.ParentId;
            affected = await BoundParentRecalculator.RecalculateBoundParentsAsync(
              db,
              PlatformApp,
              Request.ParentService,
              parent.BoundParentService,
              parent.BoundParentServiceType,
              [oldParentId, newParentId],
              mutationCt,
              requestParentId: Request.ParentId);
          }

          actionEntity = entity;
          return new FormActionDto
          {
            Outcome = MutationOutcome.Committed,
            OpenScreen = ctx.OpenScreen,
            OpenId = ctx.OpenId,
            Title = ctx.NoticeTitle,
            Messages = ctx.NoticeMessages,
            Warnings = ctx.Warnings.Count > 0 ? ctx.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList() : null,
            Affected = affected
          };
        }
        catch (DbUpdateConcurrencyException)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      },
      ct,
      PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && actionEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(actionEntity, db, ct);
    }

    return result;
  }

  public virtual async Task<FormActionDto> MoveStatusAsync(
    long id,
    string transitionName,
    JsonElement? body,
    int? ver = null,
    CancellationToken ct = default)
  {
    var curReq = Request;
    Request = new FormRequest
    {
      Id = id,
      Ver = ver ?? curReq.Ver,
      ParentId = curReq.ParentId,
      ParentService = curReq.ParentService,
      IdempotencyKey = curReq.IdempotencyKey,
      RawPayload = curReq.RawPayload
    };
    IsNew = false;

    var parent = GetParent();
    TEntity? movedEntity = null;
    var actionPath = $"{Service}/{StatusMoveNavBuilder.BuildMoveStatusMethod(transitionName)}";

    var result = await MutationRunner.RunIdempotentMutationAsync<FormActionDto>(
      db,
      actionPath,
      Request.IdempotencyKey,
      ResolveIdempotentPayload(Request.IdempotencyKey, Request.RawPayload, body),
      async mutationCt =>
      {
        var context = new MutationContext(db);
        IRecordStatuses<TEntity>? statusFlow = null;
        RecordTransition? transition = null;
        object? input = null;

        var (entity, refusal) = await LockAndLoadExistingAsync<FormActionDto>(
          id,
          parent,
          async (candidate, stepCt) =>
          {
            CurrentEntity = candidate;
            var elements = await BuildElementsAsync(stepCt);
            if (elements.StatusFlow is not IRecordStatuses<TEntity> resolvedFlow)
            {
              throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
            }

            statusFlow = resolvedFlow;
            (statusFlow as IPlatformService)?.BindContext(PlatformApp);

            transition = statusFlow.AllTransitions.FirstOrDefault(t =>
              StatusMoveNavBuilder.MatchesTransition(t.Name, transitionName));

            if (transition == null ||
                !StatusMoveNavBuilder.CanMoveOnScreen(
                  Feature,
                  User,
                  transition,
                  IsServiceLockedReadOnly,
                  elements.StatusFlowAllowsReadOnly,
                  ResolveParentChildBuilder()?.IsReadOnly == true,
                  typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity))))
            {
              return (false, new FormActionDto
              {
                Outcome = MutationOutcome.Refused,
                Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
              });
            }

            input = transition.Ask?.Read(body);
            if (transition.Ask != null)
            {
              var askFailures = await transition.Ask.ValidateAsync(input, db, PlatformApp, L, stepCt);
              if (askFailures.Count > 0)
              {
                return (false, new FormActionDto
                {
                  Outcome = MutationOutcome.Refused,
                  Messages = [.. askFailures]
                });
              }
            }

            await statusFlow.LockRecordsAsync(context, [candidate], stepCt);
            return (true, null);
          },
          mutationCt);

        if (refusal != null)
        {
          return refusal;
        }

        CurrentEntity = entity!;

        if (entity is IHasVersion versioned &&
            (!Request.Ver.HasValue || Request.Ver.Value <= 0 || versioned.Ver != Request.Ver.Value))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }

        try
        {
          var oldParentId = parent.BoundProperty != null ? parent.BoundProperty.GetValue(entity) as long? : null;
          await statusFlow!.MoveAsync(context, [entity!], transition!.Name, input, mutationCt);

          if (context.Errors.Count > 0)
          {
            return new FormActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = context.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
            };
          }

          List<AffectedDto>? affected = null;
          if (parent.BoundProperty != null || parent.BoundParentServiceType != null)
          {
            var newParentId = parent.BoundProperty != null
              ? parent.BoundProperty.GetValue(entity) as long? ?? Request.ParentId
              : Request.ParentId;
            affected = await BoundParentRecalculator.RecalculateBoundParentsAsync(
              db,
              PlatformApp,
              Request.ParentService,
              parent.BoundParentService,
              parent.BoundParentServiceType,
              [oldParentId, newParentId],
              mutationCt,
              requestParentId: Request.ParentId);
          }

          movedEntity = entity;
          return new FormActionDto
          {
            Outcome = MutationOutcome.Committed,
            Warnings = context.Warnings.Count > 0 ? context.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList() : null,
            Affected = affected
          };
        }
        catch (DbUpdateConcurrencyException)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      },
      ct,
      PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && PlatformApp != null && movedEntity != null)
    {
      await PlatformApp.ClearAuthCacheForEntityAsync(movedEntity, db, ct);
    }

    return result;
  }

  async Task<IReadOnlyList<(GridNavActionModel Model, bool IsInMenu, bool IsAllowedInReadOnly, IReadOnlyList<RecordAvailability> Offers)>> IFormService.BuildInheritedGridActionsAsync(
    string gridServicePath,
    long? parentId,
    string? parentService,
    bool isGridReadOnly,
    bool isParentReadOnly,
    CancellationToken ct)
  {
    SetRequest(new FormRequest { ParentId = parentId, ParentService = parentService });
    if (parentId.HasValue || !string.IsNullOrEmpty(parentService))
    {
      await PreloadParentRecordAsync(GetParent(), ct);
    }

    var effectiveParentReadOnly = isParentReadOnly || (await ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    var actions = await BuildActionsAsync(ct);
    var list = new List<(GridNavActionModel, bool, bool, IReadOnlyList<RecordAvailability>)>();
    foreach (var entry in actions.Entries)
    {
      var isReadOnly = isGridReadOnly || IsReadOnly;
      if (entry.Permission.IsPermitted(Feature, User, entry.Handle, isReadOnly, entry.IsAllowedInReadOnly, effectiveParentReadOnly))
      {
        var gridModel = await entry.BuildGridModelAsync(GetType(), Feature, gridServicePath, db, PlatformApp, User, L, ct);
        list.Add((gridModel, entry.IsInMenu, entry.IsAllowedInReadOnly, entry.Offers));
      }
    }

    return list;
  }

  async Task<IReadOnlyList<(string Key, IReadOnlyList<RecordAvailability> Offers)>> IFormService.GetActionOffersAsync(
    long? parentId,
    string? parentService,
    CancellationToken ct)
  {
    SetRequest(new FormRequest { ParentId = parentId, ParentService = parentService });
    var actions = await BuildActionsAsync(ct);
    return actions.Entries
      .Where(e => e.Offers.Count > 0)
      .Select(e => (e.Key, (IReadOnlyList<RecordAvailability>)e.Offers))
      .ToList();
  }

  async Task<bool> IFormService.IsActionPermittedForGridAsync(
    string actionKey,
    bool isGridReadOnly,
    bool isParentReadOnly,
    CancellationToken ct)
  {
    var actions = await BuildActionsAsync(ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    var effectiveParentReadOnly = isParentReadOnly || (await ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    return entry != null && entry.Permission.IsPermitted(Feature, User, entry.Handle, isGridReadOnly || IsReadOnly, entry.IsAllowedInReadOnly, effectiveParentReadOnly);
  }

  private static string? ResolveIdempotentPayload(string? idempotencyKey, string? rawPayload, JsonElement? body) =>
    !string.IsNullOrWhiteSpace(idempotencyKey) ? rawPayload ?? body?.GetRawText() : null;
}
