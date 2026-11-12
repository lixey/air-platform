using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
  where TEntity : class, IEntity, new()
{
  public virtual async Task<bool> HasActionAsync(string actionKey, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(actionKey))
    {
      return false;
    }

    var actions = new Actions();
    await SetActionsAsync(actions, ct);
    return actions.Entries.Any(e => e.Matches(actionKey));
  }

  public virtual async Task<FormActionDto?> ExecuteDeclaredActionAsync(
    string actionKey,
    FormRequest request,
    JsonElement? body,
    CancellationToken ct = default)
  {
    SetRequest(request);
    IsNew = false;

    var probeActions = new Actions();
    await SetActionsAsync(probeActions, ct);
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
      request.RawPayload ?? body?.GetRawText(),
      async mutationCt =>
      {
        var entity = await LockAndLoadExistingAsync(request.Id.Value, parent, mutationCt);
        CurrentEntity = entity;

        var actions = new Actions();
        await SetActionsAsync(actions, mutationCt);
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
            affected = await ParentScopeValidator.RecalculateBoundParentsAsync(
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
    Request = new FormRequest
    {
      Id = id,
      Ver = ver ?? Request.Ver,
      ParentId = Request.ParentId,
      ParentService = Request.ParentService,
      IdempotencyKey = Request.IdempotencyKey,
      RawPayload = Request.RawPayload
    };
    IsNew = false;

    var parent = GetParent();
    TEntity? movedEntity = null;
    var actionPath = $"{Service}/move-status/{transitionName.ToLowerInvariant()}";

    var result = await MutationRunner.RunIdempotentMutationAsync<FormActionDto>(
      db,
      actionPath,
      Request.IdempotencyKey,
      Request.RawPayload ?? body?.GetRawText(),
      async mutationCt =>
      {
        var requestedParentId = Request.ParentId;
        var candidate = await Reachable(asTracking: false).FirstOrDefaultAsync(e => e.Id == id, mutationCt)
          ?? throw new KeyNotFoundException(L["FormNotFound"]);

        if (parent.BoundProperty != null &&
            (parent.MatchesService(Request.ParentService) || (await ResolveParentChildBuilderAsync(requirePreset: true, mutationCt))?.PresetProvider == null))
        {
          var probeParentId = parent.BoundProperty.GetValue(candidate) as long?;
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

        var lockedParentId = Request.ParentId;
        await EnsureParentScopeAsync(parent, lockParent: true, mutationCt);

        CurrentEntity = candidate;
        var elements = new Elements();
        await SetElementsAsync(elements, mutationCt);
        if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
        {
          throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
        }

        if (IsReadOnly && !elements.StatusFlowAllowsReadOnly)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]
          };
        }

        if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
        {
          flowSvc.AttachPlatformApp(PlatformApp);
        }

        var transition = statusFlow.Transitions.FirstOrDefault(t =>
          string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));

        if (transition == null || !StatusMoveNavBuilder.CanMove(Feature, User, transition.Name))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var input = transition.Ask?.Read(body);
        if (transition.Ask != null)
        {
          var askFailures = await transition.Ask.ValidateAsync(input, db, PlatformApp, L, mutationCt);
          if (askFailures.Count > 0)
          {
            return new FormActionDto
            {
              Outcome = MutationOutcome.Refused,
              Messages = [.. askFailures]
            };
          }
        }

        var context = new MutationContext(db);
        await statusFlow.LockRecordsAsync(context, [candidate], mutationCt);
        await MutationContext.AcquireLocksAsync(db, typeof(TEntity), [[id]], mutationCt);

        if (db.Set<TEntity>().Local.FirstOrDefault(e => e.Id == id) is { } localBeforeLock &&
            db.Entry(localBeforeLock).State == EntityState.Unchanged &&
            db.Database.IsRelational())
        {
          await db.Entry(localBeforeLock).ReloadAsync(mutationCt);
        }

        var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, mutationCt)
          ?? throw new KeyNotFoundException(L["FormNotFound"]);

        Request.ParentId = requestedParentId;
        RestoreBoundParentId(entity, parent);
        if (Request.ParentId != lockedParentId)
        {
          await EnsureParentScopeAsync(parent, lockParent: true, mutationCt);
        }

        CurrentEntity = entity;

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
          await statusFlow.MoveAsync(context, [entity], transition.Name, input, mutationCt);

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
            affected = await ParentScopeValidator.RecalculateBoundParentsAsync(
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
}
