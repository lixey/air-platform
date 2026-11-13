using Platform.App.Internals;
using Platform.App.Screens.Internals;

namespace Platform.App.Form.Internals;

internal static class FormActionRunner
{
  public static async Task<bool> HasActionAsync<TEntity>(
    FormService<TEntity> form,
    string actionKey,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    if (string.IsNullOrWhiteSpace(actionKey))
    {
      return false;
    }

    var actions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(actions, ct);
    return actions.Entries.Any(e => e.Matches(actionKey));
  }

  public static async Task<object?> GetActionInputAsync<TEntity>(
    FormService<TEntity> form,
    string actionKey,
    FormRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    form.SetRequest(request);
    try
    {
      await form.EnsureEntityAndParentLoadedAsync(ct);
    }
    catch (KeyNotFoundException)
    {
      return null;
    }

    if (!form.IsNewRecord && form.Entity == null)
    {
      return null;
    }

    var actions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(actions, ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    var isParentReadOnly = (await form.ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    if (entry == null || !entry.Permission.IsPermitted(form.Feature, form.User, entry.Handle, form.IsFormReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
    {
      return null;
    }

    var defaultProvider = entry.DefaultProvider != null
      ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(form.Entity, token))
      : null;
    return await StatusMoveNavBuilder.ResolveActionDefaultsAsync(defaultProvider, entry.Ask, form.DbContext, form.PlatformApp, ct);
  }

  public static async Task<FormActionDto?> ExecuteDeclaredActionAsync<TEntity>(
    FormService<TEntity> form,
    string actionKey,
    FormRequest request,
    JsonElement? body,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    form.SetRequest(request);
    form.IsNewRecord = false;

    var probeActions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(probeActions, ct);
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
        Messages = [new MessageDto { Detail = form.L["FormNotFound"], Type = MessageType.Error }]
      };
    }

    var db = form.DbContext;
    var parent = form.GetParentDeclaration();
    TEntity? actionEntity = null;
    var actionPath = $"{form.Service}/{probeEntry.Method}";

    var result = await MutationRunner.RunIdempotentMutationAsync<FormActionDto>(
      db,
      actionPath,
      request.IdempotencyKey,
      request.RawPayload ?? body?.GetRawText(),
      async mutationCt =>
      {
        var entity = await FormSavePipeline.LockAndLoadExistingAsync(form, request.Id.Value, parent, mutationCt);
        form.Entity = entity;

        var actions = new FormService<TEntity>.Actions();
        await form.InvokeSetActionsAsync(actions, mutationCt);
        var entry = actions.Entries.FirstOrDefault(e => e.Matches(actionKey));
        if (entry?.Handler == null)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = form.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var isParentReadOnly = (await form.ResolveParentChildBuilderAsync(ct: mutationCt))?.IsReadOnly == true;
        if (!entry.Permission.IsPermitted(form.Feature, form.User, entry.Handle, form.IsFormReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = form.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        if (entity is IHasVersion versioned &&
            (!request.Ver.HasValue || request.Ver.Value <= 0 || versioned.Ver != request.Ver.Value))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = form.L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }

        object? askInput = null;
        if (entry.Ask != null)
        {
          askInput = entry.Ask.Read(body ?? request.Value);
          var askFailures = await entry.Ask.ValidateAsync(askInput, db, form.PlatformApp, form.L, mutationCt);
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
                  Messages = [new MessageDto { Detail = form.L[offer.Reason], RecordId = entity.Id, Type = MessageType.Error }]
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
                ctx.AddError("form.guard", form.L[refusal], entity.Id, path);
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
              ? parent.BoundProperty.GetValue(entity) as long? ?? form.CurrentRequest.ParentId
              : form.CurrentRequest.ParentId;
            affected = await ParentScopeValidator.RecalculateBoundParentsAsync(
              db,
              form.PlatformApp,
              form.CurrentRequest.ParentService,
              parent.BoundParentService,
              parent.BoundParentServiceType,
              [oldParentId, newParentId],
              mutationCt,
              requestParentId: form.CurrentRequest.ParentId);
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
            Messages = [new MessageDto { Detail = form.L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      },
      ct,
      form.PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && form.PlatformApp != null && actionEntity != null)
    {
      await form.PlatformApp.ClearAuthCacheForEntityAsync(actionEntity, db, ct);
    }

    return result;
  }

  public static async Task<FormActionDto> MoveStatusAsync<TEntity>(
    FormService<TEntity> form,
    long id,
    string transitionName,
    JsonElement? body,
    int? ver,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var curReq = form.CurrentRequest;
    form.CurrentRequest = new FormRequest
    {
      Id = id,
      Ver = ver ?? curReq.Ver,
      ParentId = curReq.ParentId,
      ParentService = curReq.ParentService,
      IdempotencyKey = curReq.IdempotencyKey,
      RawPayload = curReq.RawPayload
    };
    form.IsNewRecord = false;

    var db = form.DbContext;
    var parent = form.GetParentDeclaration();
    TEntity? movedEntity = null;
    var actionPath = $"{form.Service}/move-status/{transitionName.ToLowerInvariant()}";

    var result = await MutationRunner.RunIdempotentMutationAsync<FormActionDto>(
      db,
      actionPath,
      form.CurrentRequest.IdempotencyKey,
      form.CurrentRequest.RawPayload ?? body?.GetRawText(),
      async mutationCt =>
      {
        var requestedParentId = form.CurrentRequest.ParentId;
        var candidate = await form.ReachableQuery(asTracking: false).FirstOrDefaultAsync(e => e.Id == id, mutationCt)
          ?? throw new KeyNotFoundException(form.L["FormNotFound"]);

        if (parent.BoundProperty != null &&
            (parent.MatchesService(form.CurrentRequest.ParentService) || (await form.ResolveParentChildBuilderAsync(requirePreset: true, mutationCt))?.PresetProvider == null))
        {
          var probeParentId = parent.BoundProperty.GetValue(candidate) as long?;
          if (requestedParentId is > 0 && probeParentId is > 0 && probeParentId.Value != requestedParentId.Value)
          {
            throw new FormRefusedException(MutationOutcome.Refused, form.L["MutationRefused"], [new MessageDto { Detail = form.L["FormNotFound"], Type = MessageType.Error }]);
          }

          if (!parent.IsOptional || !form.CurrentRequest.ParentId.HasValue)
          {
            if (probeParentId.HasValue)
            {
              form.CurrentRequest.ParentId = probeParentId.Value;
            }
          }
        }

        var lockedParentId = form.CurrentRequest.ParentId;
        await form.EnsureParentScopeInternalAsync(parent, lockParent: true, mutationCt);

        form.Entity = candidate;
        var elements = new FormService<TEntity>.Elements();
        await form.InvokeSetElementsAsync(elements, mutationCt);
        if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
        {
          throw new InvalidOperationException($"Form '{form.GetType().Name}' does not use a status flow.");
        }

        if (form.IsFormReadOnly && !elements.StatusFlowAllowsReadOnly)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = form.L["FormReadOnly"], Type = MessageType.Error }]
          };
        }

        if (form.PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
        {
          flowSvc.AttachPlatformApp(form.PlatformApp);
        }

        var transition = statusFlow.Transitions.FirstOrDefault(t =>
          string.Equals(t.Name, transitionName, StringComparison.OrdinalIgnoreCase));

        if (transition == null || !StatusMoveNavBuilder.CanMove(form.Feature, form.User, transition.Name))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Messages = [new MessageDto { Detail = form.L["MutationRefused"], Type = MessageType.Error }]
          };
        }

        var input = transition.Ask?.Read(body);
        if (transition.Ask != null)
        {
          var askFailures = await transition.Ask.ValidateAsync(input, db, form.PlatformApp, form.L, mutationCt);
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

        var entity = await form.ReachableQuery(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, mutationCt)
          ?? throw new KeyNotFoundException(form.L["FormNotFound"]);

        form.CurrentRequest.ParentId = requestedParentId;
        form.RestoreBoundParentId(entity, parent);
        if (form.CurrentRequest.ParentId != lockedParentId)
        {
          await form.EnsureParentScopeInternalAsync(parent, lockParent: true, mutationCt);
        }

        form.Entity = entity;

        if (entity is IHasVersion versioned &&
            (!form.CurrentRequest.Ver.HasValue || form.CurrentRequest.Ver.Value <= 0 || versioned.Ver != form.CurrentRequest.Ver.Value))
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Messages = [new MessageDto { Detail = form.L["FormRecordChanged"], Type = MessageType.Error }]
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
              ? parent.BoundProperty.GetValue(entity) as long? ?? form.CurrentRequest.ParentId
              : form.CurrentRequest.ParentId;
            affected = await ParentScopeValidator.RecalculateBoundParentsAsync(
              db,
              form.PlatformApp,
              form.CurrentRequest.ParentService,
              parent.BoundParentService,
              parent.BoundParentServiceType,
              [oldParentId, newParentId],
              mutationCt,
              requestParentId: form.CurrentRequest.ParentId);
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
            Messages = [new MessageDto { Detail = form.L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      },
      ct,
      form.PlatformApp?.Services);

    if (result.Outcome == MutationOutcome.Committed && form.PlatformApp != null && movedEntity != null)
    {
      await form.PlatformApp.ClearAuthCacheForEntityAsync(movedEntity, db, ct);
    }

    return result;
  }

  public static async Task<IReadOnlyList<(GridNavActionModel Model, bool IsInMenu, bool IsAllowedInReadOnly, IReadOnlyList<RecordAvailability> Offers)>> BuildInheritedGridActionsAsync<TEntity>(
    FormService<TEntity> form,
    string gridServicePath,
    long? parentId,
    string? parentService,
    bool isGridReadOnly,
    bool isParentReadOnly,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    form.SetRequest(new FormRequest { ParentId = parentId, ParentService = parentService });
    if (parentId.HasValue || !string.IsNullOrEmpty(parentService))
    {
      await form.PreloadParentRecordInternalAsync(form.GetParentDeclaration(), ct);
    }

    var effectiveParentReadOnly = isParentReadOnly || (await form.ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    var actions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(actions, ct);
    var list = new List<(GridNavActionModel, bool, bool, IReadOnlyList<RecordAvailability>)>();
    foreach (var entry in actions.Entries)
    {
      var isReadOnly = isGridReadOnly || form.IsFormReadOnly;
      if (entry.Permission.IsPermitted(form.Feature, form.User, entry.Handle, isReadOnly, entry.IsAllowedInReadOnly, effectiveParentReadOnly))
      {
        var gridModel = await entry.BuildGridModelAsync(form.GetType(), form.Feature, gridServicePath, form.DbContext, form.PlatformApp, form.User, form.L, ct);
        list.Add((gridModel, entry.IsInMenu, entry.IsAllowedInReadOnly, entry.Offers));
      }
    }

    return list;
  }

  public static async Task<IReadOnlyList<(string Key, IReadOnlyList<RecordAvailability> Offers)>> GetActionOffersAsync<TEntity>(
    FormService<TEntity> form,
    long? parentId,
    string? parentService,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    form.SetRequest(new FormRequest { ParentId = parentId, ParentService = parentService });
    var actions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(actions, ct);
    return actions.Entries
      .Where(e => e.Offers.Count > 0)
      .Select(e => (e.Key, (IReadOnlyList<RecordAvailability>)e.Offers))
      .ToList();
  }

  public static async Task<bool> IsActionPermittedForGridAsync<TEntity>(
    FormService<TEntity> form,
    string actionKey,
    bool isGridReadOnly,
    bool isParentReadOnly,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var actions = new FormService<TEntity>.Actions();
    await form.InvokeSetActionsAsync(actions, ct);
    var entry = actions.Entries.FirstOrDefault(a => a.Matches(actionKey));
    var effectiveParentReadOnly = isParentReadOnly || (await form.ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    return entry != null && entry.Permission.IsPermitted(form.Feature, form.User, entry.Handle, isGridReadOnly || form.IsFormReadOnly, entry.IsAllowedInReadOnly, effectiveParentReadOnly);
  }
}
