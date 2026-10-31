namespace Platform.App.Form;

public abstract partial class FormService<TEntity, TInput>
  where TEntity : class, IEntity, new()
  where TInput : class, new()
{
  protected async Task<FormActionDto> ExecuteActionAsync(
    FormRequest request,
    Func<FormActionContext<TEntity>, TEntity, CancellationToken, Task> handler,
    CancellationToken cancellationToken = default,
    [System.Runtime.CompilerServices.CallerMemberName] string? actionName = null)
  {
    Request = request;
    IsNew = false;
    if (request.Id is not > 0)
    {
      return new FormActionDto
      {
        Outcome = MutationOutcome.Refused,
        Warnings = [new MessageDto { Detail = L["FormNotFound"], Type = MessageType.Error }]
      };
    }

    var parent = GetParent();

    return await db.RunMutationAsync(async mutationCt =>
    {
      await MutationContext.AcquireLocksAsync(db, typeof(TEntity), [[request.Id.Value]], mutationCt);
      var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == request.Id.Value, mutationCt)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);

      if (entity is IHasVersion versioned)
      {
        if (!request.Ver.HasValue || request.Ver.Value <= 0 || versioned.Ver != request.Ver.Value)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Conflict,
            Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
          };
        }
      }

      RestoreBoundParentId(entity, parent);
      await EnsureParentScopeAsync(parent, lockParent: true, mutationCt);

      var nav = new Nav(Service, L);
      await SetNavAsync(nav, mutationCt);
      var navEntry = !string.IsNullOrEmpty(actionName)
        ? nav.Actions.FirstOrDefault(a => string.Equals(a.Method.Method.Name, actionName, StringComparison.OrdinalIgnoreCase))
        : null;

      if (IsReadOnly && navEntry?.IsAllowedInReadOnly != true)
      {
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]
        };
      }

      if (navEntry is { Offers.Count: > 0 })
      {
        foreach (var offer in navEntry.Offers)
        {
          if (offer.Condition is Expression<Func<TEntity, bool>> cond)
          {
            var passes = await db.Set<TEntity>().Where(e => e.Id == entity.Id).AnyAsync(cond, mutationCt);
            if (!passes)
            {
              return new FormActionDto
              {
                Outcome = MutationOutcome.Refused,
                Warnings = [new MessageDto { Detail = L[offer.Reason], RecordId = entity.Id, Type = MessageType.Error }]
              };
            }
          }
        }
      }

      var ctx = new FormActionContext<TEntity>(entity, ct => db.SaveChangesAsync(ct), db);
      await handler(ctx, entity, mutationCt);
      if (ctx.Errors.Count > 0)
      {
        return new FormActionDto
        {
          Outcome = MutationOutcome.Refused,
          Warnings = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList()
        };
      }

      if (db.ChangeTracker.HasChanges())
      {
        await db.SaveChangesAsync(mutationCt);
      }

      if (PlatformApp != null)
      {
        await PlatformApp.ClearAuthCacheForEntityAsync(entity, db, mutationCt);
      }

      return new FormActionDto
      {
        Outcome = MutationOutcome.Committed,
        OpenScreen = ctx.OpenScreen,
        OpenId = ctx.OpenId,
        Warnings = ctx.Warnings.Count > 0 ? ctx.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList() : null
      };
    }, ct: cancellationToken);
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
      ParentService = Request.ParentService
    };
    IsNew = false;

    var parent = GetParent();

    return await db.RunMutationAsync(async mutationCt =>
    {
      await MutationContext.AcquireLocksAsync(db, typeof(TEntity), [[id]], mutationCt);
      var trackedExisting = db.Set<TEntity>().Local.FirstOrDefault(e => e.Id == id);
      if (trackedExisting != null && db.Entry(trackedExisting).State == EntityState.Unchanged)
      {
        await db.Entry(trackedExisting).ReloadAsync(mutationCt);
      }

      var entity = await Reachable(asTracking: true).FirstOrDefaultAsync(e => e.Id == id, mutationCt)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);

      RestoreBoundParentId(entity, parent);
      await EnsureParentScopeAsync(parent, lockParent: true, mutationCt);

      CurrentEntity = entity;
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
          Warnings = [new MessageDto { Detail = L["FormReadOnly"], Type = MessageType.Error }]
        };
      }

      if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
      {
        flowSvc.AttachPlatformApp(PlatformApp);
      }

      if (entity is IHasVersion versioned &&
          (!Request.Ver.HasValue || Request.Ver.Value <= 0 || versioned.Ver != Request.Ver.Value))
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

      try
      {
        var context = new MutationContext(db);
        await statusFlow.MoveAsync(context, [entity], transition.Name, input, mutationCt);

        if (context.Errors.Count > 0)
        {
          return new FormActionDto
          {
            Outcome = MutationOutcome.Refused,
            Warnings = context.Errors.Select(e => e.ToMessage()).ToList()
          };
        }

        return new FormActionDto
        {
          Outcome = MutationOutcome.Committed,
          Warnings = context.Warnings.Count > 0 ? context.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList() : null
        };
      }
      catch (DbUpdateConcurrencyException)
      {
        return new FormActionDto
        {
          Outcome = MutationOutcome.Conflict,
          Warnings = [new MessageDto { Detail = L["FormRecordChanged"], Type = MessageType.Error }]
        };
      }
    }, ct: ct);
  }
}
