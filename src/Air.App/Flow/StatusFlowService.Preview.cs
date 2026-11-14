using Platform.App.Screens.Internals;

namespace Platform.App.Flow;

public abstract partial class StatusFlowService<TEntity>
  where TEntity : class, IEntity, new()
{
  public virtual async Task<FlowDescriptionDto> DescribeFlowAsync(CancellationToken cancellationToken = default)
  {
    EnsureDeclared();
    var statusPath = _statusMember != null
      ? StatusMoveNavBuilder.ResolveStatusPath(_statusMember)
      : "docStatusId";

    var localizedStatuses = _statuses
      .Select(s => new OptionDto
      {
        Id = s.Id,
        Name = L[s.Name],
        Color = s.Color,
        Title = s.Title != null ? L[s.Title] : null
      })
      .ToList();

    var orderedPermitted = _publicTransitions
      .Where(IsTransitionPermitted)
      .OrderBy(t =>
      {
        var idx = _statuses.FindIndex(s => s.Id == t.To);
        return idx >= 0 ? idx : int.MaxValue;
      })
      .ToList();

    var descriptors = new List<FlowTransitionDescriptorDto>(orderedPermitted.Count);
    foreach (var t in orderedPermitted)
    {
      var target = _statuses.FirstOrDefault(s => s.Id == t.To);
      var label = ResolveTransitionLabel(t, target);
      var title = ResolveTransitionTitle(t, target);
      var confirm = !string.IsNullOrEmpty(t.Question) ? L[t.Question] : null;
      IReadOnlyList<FormElementDto> askElements = [];
      if (t.Asked != null)
      {
        var resolved = await t.Asked.BuildResolvedElementsAsync(GetType(), Feature, Db, PlatformApp, User, L, cancellationToken);
        askElements = StatusMoveNavBuilder.LocalizeElements(resolved, L);
      }

      descriptors.Add(new FlowTransitionDescriptorDto
      {
        Name = t.Name,
        Label = label,
        Title = title,
        Confirm = confirm,
        From = t.From,
        To = t.To,
        ToStatusName = target != null ? L[target.Name] : NameOf(t.To),
        ToStatusColor = target?.Color,
        AskElements = askElements
      });
    }

    return new FlowDescriptionDto
    {
      StatusPath = statusPath,
      Statuses = localizedStatuses,
      Transitions = descriptors
    };
  }

  public virtual async Task<StatusMovePreviewDto> PreviewMoveAsync(
    object record,
    string transitionName,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    CancellationToken cancellationToken = default)
  {
    EnsureDeclared();
    if (record is not TEntity entity)
    {
      throw new ArgumentException($"Expected record of type '{typeof(TEntity).Name}'.", nameof(record));
    }

    var transition = FindPublicTransition(transitionName);
    if (transition == null)
    {
      var unknownMsg = L["StatusTransitionUnknown", transitionName];
      throw new FormRefusedException(
        MutationOutcome.Refused,
        unknownMsg,
        [new MessageDto { Code = "status.unknown", Detail = unknownMsg, Type = MessageType.Error }]);
    }

    if (!IsTransitionPermitted(transition))
    {
      var refusedMsg = L["MutationRefused"];
      throw new FormRefusedException(
        MutationOutcome.Refused,
        refusedMsg,
        [new MessageDto { Code = "status.permission", Detail = refusedMsg, Type = MessageType.Error }]);
    }

    var statusReader = _statusReader ??= _statusMember!.Compile();
    var currentStatus = statusReader(entity);
    if (!transition.From.Contains(currentStatus))
    {
      var refusal = transition is Transition nestedTransition
        ? await RefusalAsync(nestedTransition, currentStatus, cancellationToken)
        : L["StatusRefusesMove", NameOf(currentStatus), NameOf(transition.To)];
      throw new FormRefusedException(
        MutationOutcome.Refused,
        refusal,
        [new MessageDto { Code = "status.transition", Detail = refusal, RecordId = entity.Id > 0 ? entity.Id : null, Type = MessageType.Error }]);
    }

    if (entity.Id > 0 && transition.Availability.Count > 0)
    {
      foreach (var offer in transition.Availability)
      {
        if (offer.Condition is Expression<Func<TEntity, bool>> cond)
        {
          var passes = await Db.Set<TEntity>()
            .Where(e => e.Id == entity.Id)
            .AnyAsync(cond, cancellationToken);
          if (!passes)
          {
            var availMsg = L[offer.Reason];
            throw new FormRefusedException(
              MutationOutcome.Refused,
              availMsg,
              [new MessageDto { Code = "status.availability", Detail = availMsg, RecordId = entity.Id, Type = MessageType.Error }]);
          }
        }
      }
    }

    IReadOnlyList<FormElementDto> askElements = [];
    object? boundInput = null;
    var askValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    if (transition.Asked != null)
    {
      var resolved = await transition.Asked.BuildResolvedElementsAsync(GetType(), Feature, Db, PlatformApp, User, L, cancellationToken);
      askElements = StatusMoveNavBuilder.LocalizeElements(resolved, L);
      boundInput = transition.Asked.Read(askInput);
      var askFailures = await transition.Asked.ValidateAsync(boundInput, Db, PlatformApp, L, cancellationToken, skipValidationPaths);
      if (askFailures.Count > 0)
      {
        throw new FormRefusedException(
          MutationOutcome.Refused,
          askFailures[0].Detail ?? L["FormInvalid"],
          askFailures);
      }

      if (boundInput != null)
      {
        var serialized = JsonSerializer.Serialize(boundInput, PlatformJsonOptions.DefaultOptions);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized, PlatformJsonOptions.DefaultOptions);
        if (parsed != null)
        {
          foreach (var (k, v) in parsed)
          {
            askValues[k] = v;
          }
        }
      }
    }

    var previewContext = new MutationContext(Db);
    if (entity.Id > 0 && transition.Guards.Count > 0)
    {
      _executionDepth++;
      try
      {
        var move = new Move
        {
          Record = entity,
          From = currentStatus,
          To = transition.To,
          Transition = transition.Name,
          Note = transition.NoteOf(boundInput),
          UserId = Db.TenantContext.UserId,
          At = Time.GetUtcNow(),
          Context = previewContext,
          Input = boundInput
        };

        foreach (var (passes, refusal, path) in transition.Guards)
        {
          if (path != null && skipValidationPaths?.Contains(path) == true)
          {
            continue;
          }

          var errorsBefore = previewContext.Errors.Count;
          var guardPassed = await passes(move, cancellationToken);
          if (!guardPassed && previewContext.Errors.Count == errorsBefore)
          {
            previewContext.AddError("status.guard", L[refusal], entity.Id, path);
          }

          if (!guardPassed || previewContext.Errors.Count > errorsBefore)
          {
            var errors = previewContext.Errors
              .Select(e =>
              {
                var msg = e.ToMessage(MessageType.Error);
                msg.Detail ??= e.Title;
                return msg;
              })
              .ToList();
            throw new FormRefusedException(
              MutationOutcome.Refused,
              errors[0].Detail ?? L[refusal],
              errors);
          }
        }
      }
      finally
      {
        if (--_executionDepth == 0)
        {
          _executionItems = null;
        }
      }
    }

    var targetStatus = _statuses.FirstOrDefault(s => s.Id == transition.To);
    var statusPath = _statusMember != null
      ? StatusMoveNavBuilder.ResolveStatusPath(_statusMember)
      : "docStatusId";

    return new StatusMovePreviewDto
    {
      RecordId = entity.Id,
      Ver = (entity as IHasVersion)?.Ver,
      StatusPath = statusPath,
      TransitionName = transition.Name,
      TransitionLabel = ResolveTransitionLabel(transition, targetStatus),
      CurrentStatusId = currentStatus,
      CurrentStatusName = NameOf(currentStatus),
      TargetStatusId = transition.To,
      TargetStatusName = targetStatus != null ? L[targetStatus.Name] : NameOf(transition.To),
      TargetStatusColor = targetStatus?.Color,
      AskElements = askElements,
      AskValues = askValues,
      Warnings = previewContext.Warnings.Count > 0
        ? previewContext.Warnings.Select(w => w.ToMessage(MessageType.Warning)).ToList()
        : []
    };
  }

  private Transition<TEntity>? FindPublicTransition(string transitionName)
  {
    if (string.IsNullOrWhiteSpace(transitionName))
    {
      return null;
    }

    var byKey = _publicTransitions.FirstOrDefault(t => StatusMoveNavBuilder.MatchesTransition(t.Name, transitionName));
    if (byKey != null)
    {
      return byKey;
    }

    return _publicTransitions.FirstOrDefault(t =>
    {
      var target = _statuses.FirstOrDefault(s => s.Id == t.To);
      return string.Equals(ResolveTransitionLabel(t, target), transitionName, StringComparison.OrdinalIgnoreCase);
    });
  }

  private string? ResolveTransitionTitle(Transition<TEntity> transition, OptionDto? target)
  {
    if (!string.IsNullOrEmpty(transition.TitleText))
    {
      return L[transition.TitleText];
    }

    return target?.Title != null ? L[target.Title] : null;
  }

  private string ResolveTransitionLabel(Transition<TEntity> transition, OptionDto? target)
  {
    if (!string.IsNullOrEmpty(transition.LabelText))
    {
      return L[transition.LabelText];
    }

    var verbLabel = !string.IsNullOrEmpty(Feature.Module)
      ? L.TextOr($"{Feature.Module}: {transition.Name}", L[transition.Name])
      : L[transition.Name];
    var targetLabel = target != null ? L[target.Name] : verbLabel;
    return verbLabel != transition.Name ? verbLabel : targetLabel;
  }
}
