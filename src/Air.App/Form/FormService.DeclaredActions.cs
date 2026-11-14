using Platform.App.Screens.Internals;

namespace Platform.App.Form;

public abstract partial class FormService<TEntity>
{
  public virtual async Task<FlowDescriptionDto?> DescribeStatusFlowAsync(CancellationToken ct = default)
  {
    var elements = await ProbeElementsSafeAsync(ct);
    if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      return null;
    }

    (statusFlow as IPlatformService)?.BindContext(PlatformApp);
    var desc = await statusFlow.DescribeFlowAsync(ct);
    if (typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) ||
        IsServiceLockedReadOnly && !elements.StatusFlowAllowsReadOnly)
    {
      return new FlowDescriptionDto
      {
        StatusPath = desc.StatusPath,
        Statuses = desc.Statuses,
        Transitions = []
      };
    }

    return desc;
  }

  public virtual async Task<StatusMovePreviewDto> PreviewMoveStatusAsync(
    long? id,
    string transitionName,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    CancellationToken ct = default)
  {
    if (id is > 0)
    {
      Request = new FormRequest
      {
        Id = id.Value,
        ParentId = Request.ParentId,
        ParentService = Request.ParentService
      };
      IsNew = false;
      var elements = await EnsureElementsAsync(ct, reload: true);
      if (CurrentEntity == null)
      {
        throw new KeyNotFoundException(L["FormNotFound"]);
      }

      if (elements.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
      {
        throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
      }

      (statusFlow as IPlatformService)?.BindContext(PlatformApp);
      EnsureScreenAllowsMove(elements, statusFlow, transitionName);

      var preview = await statusFlow.PreviewMoveAsync(CurrentEntity, transitionName, askInput, skipValidationPaths, ct);
      var caption = await GetRecordCaptionAsync(CurrentEntity, id.Value, ct);
      return new StatusMovePreviewDto
      {
        RecordId = preview.RecordId,
        Ver = preview.Ver,
        RecordCaption = caption,
        StatusPath = preview.StatusPath,
        TransitionName = preview.TransitionName,
        TransitionLabel = preview.TransitionLabel,
        CurrentStatusId = preview.CurrentStatusId,
        CurrentStatusName = preview.CurrentStatusName,
        TargetStatusId = preview.TargetStatusId,
        TargetStatusName = preview.TargetStatusName,
        TargetStatusColor = preview.TargetStatusColor,
        AskElements = preview.AskElements,
        AskValues = preview.AskValues,
        Warnings = preview.Warnings
      };
    }

    var probe = await ProbeElementsSafeAsync(ct);
    if (probe.StatusFlow is not IRecordStatuses<TEntity> probeFlow)
    {
      throw new InvalidOperationException($"Form '{GetType().Name}' does not use a status flow.");
    }

    (probeFlow as IPlatformService)?.BindContext(PlatformApp);
    EnsureScreenAllowsMove(probe, probeFlow, transitionName);
    var synthetic = probeFlow.Initialize(new TEntity());
    return await probeFlow.PreviewMoveAsync(synthetic, transitionName, askInput, skipValidationPaths, ct);
  }

  public virtual async Task<IReadOnlyList<DeclaredActionDescriptorDto>> DescribeDeclaredActionsAsync(
    bool automationOnly = false,
    CancellationToken ct = default)
  {
    var actions = await EnsureActionsAsync(ct);
    var isParentReadOnly = (await ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    var descriptors = new List<DeclaredActionDescriptorDto>();

    foreach (var entry in actions.Entries)
    {
      if (automationOnly && !entry.IsAgentAllowed)
      {
        continue;
      }

      if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
      {
        continue;
      }

      IReadOnlyList<FormElementDto> askElements = [];
      if (entry.Ask != null)
      {
        var resolved = await entry.Ask.BuildResolvedElementsAsync(GetType(), Feature, db, PlatformApp, User, L, ct);
        askElements = StatusMoveNavBuilder.LocalizeElements(resolved, L);
      }

      var rawLabel = entry.LabelText ?? Grid.Columns.PathToLabel(entry.Handle);
      descriptors.Add(new DeclaredActionDescriptorDto
      {
        Service = Service,
        Handle = entry.Handle,
        Method = entry.Method,
        Label = L[rawLabel],
        Title = entry.TitleText != null ? L[entry.TitleText] : null,
        Confirm = !string.IsNullOrEmpty(entry.ConfirmMessage) ? L[entry.ConfirmMessage] : null,
        Color = entry.ColorHandle ?? UiColor.Blue,
        SelectionMode = "single",
        IsAutomationAllowed = entry.IsAgentAllowed,
        AskElements = askElements
      });
    }

    return descriptors;
  }

  public virtual async Task<DeclaredActionPreviewDto?> PreviewDeclaredActionAsync(
    string actionKey,
    IReadOnlyList<long> selectedIds,
    JsonElement? askInput = null,
    IReadOnlySet<string>? skipValidationPaths = null,
    bool requireAutomationOptIn = false,
    CancellationToken ct = default)
  {
    var actions = await EnsureActionsAsync(ct, reload: true);
    var entry = actions.Entries.FirstOrDefault(e => e.Matches(actionKey));
    if (entry == null)
    {
      return null;
    }

    if (requireAutomationOptIn && !entry.IsAgentAllowed)
    {
      throw RefuseAction(L["MutationRefused"]);
    }

    if (selectedIds.Count != 1)
    {
      throw RefuseAction(L["NoRecordsSelected"]);
    }

    var targetId = selectedIds[0];
    TEntity? entity = null;
    string? caption = null;

    if (targetId > 0)
    {
      SetRequest(new FormRequest
      {
        Id = targetId,
        ParentId = Request.ParentId,
        ParentService = Request.ParentService
      });
      IsNew = false;
      _ = await EnsureElementsAsync(ct, reload: true);
      entity = CurrentEntity ?? throw new KeyNotFoundException(L["FormNotFound"]);
      caption = await GetRecordCaptionAsync(entity, targetId, ct);
    }

    var isParentReadOnly = (await ResolveParentChildBuilderAsync(ct: ct))?.IsReadOnly == true;
    if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
    {
      throw RefuseAction(L["MutationRefused"]);
    }

    if (entity != null && entry.Offers.Count > 0)
    {
      foreach (var offer in entry.Offers)
      {
        if (offer.Condition is Expression<Func<TEntity, bool>> cond)
        {
          var passes = await db.Set<TEntity>().Where(e => e.Id == entity.Id).AnyAsync(cond, ct);
          if (!passes)
          {
            throw RefuseAction(L[offer.Reason], entity.Id);
          }
        }
      }
    }

    IReadOnlyList<FormElementDto> askElements = [];
    object? boundInput = null;
    var askValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    if (entry.Ask != null)
    {
      var resolved = await entry.Ask.BuildResolvedElementsAsync(GetType(), Feature, db, PlatformApp, User, L, ct);
      askElements = StatusMoveNavBuilder.LocalizeElements(resolved, L);

      var defaultProvider = entry.DefaultProvider != null
        ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(entity, token))
        : null;
      var mergedAskJson = await MergeAskDefaultsAndInputAsync(defaultProvider, entry.Ask, askInput, ct);
      boundInput = entry.Ask.Read(mergedAskJson);

      var askFailures = await entry.Ask.ValidateAsync(boundInput, db, PlatformApp, L, ct, skipValidationPaths);
      if (askFailures.Count > 0)
      {
        throw new FormRefusedException(MutationOutcome.Refused, askFailures[0].Detail ?? L["FormInvalid"], askFailures);
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

    var warnings = new List<MessageDto>();
    if (entity != null && entry.Guards.Count > 0)
    {
      var ctx = new FormActionContext<TEntity>(entity, _ => Task.FromResult(0), db);
      foreach (var (guard, refusal, path) in entry.Guards)
      {
        if (path != null && skipValidationPaths?.Contains(path) == true)
        {
          continue;
        }

        var errorsBefore = ctx.Errors.Count;
        var guardPassed = await guard(ctx, entity, boundInput, ct);
        if (!guardPassed && ctx.Errors.Count == errorsBefore)
        {
          ctx.AddError("form.guard", L[refusal], entity.Id, path);
        }

        if (!guardPassed || ctx.Errors.Count > errorsBefore)
        {
          var errors = ctx.Errors.Select(e => e.ToMessage(MessageType.Error)).ToList();
          throw new FormRefusedException(MutationOutcome.Refused, errors[0].Detail ?? L[refusal], errors);
        }
      }

      if (ctx.Warnings.Count > 0)
      {
        warnings.AddRange(ctx.Warnings.Select(w => w.ToMessage(MessageType.Warning)));
      }
    }

    var rawLabel = entry.LabelText ?? Grid.Columns.PathToLabel(entry.Handle);
    return new DeclaredActionPreviewDto
    {
      Service = Service,
      Handle = entry.Handle,
      Method = entry.Method,
      Label = L[rawLabel],
      Color = entry.ColorHandle ?? UiColor.Blue,
      SelectionMode = "single",
      TargetIds = [targetId],
      TargetVersions = entity is IHasVersion versioned ? [versioned.Ver] : null,
      TargetCaptions = !string.IsNullOrWhiteSpace(caption) ? [caption] : [],
      AskElements = askElements,
      AskValues = askValues,
      Warnings = warnings
    };
  }

  private void EnsureScreenAllowsMove(FormElements<TEntity> elements, IRecordStatuses<TEntity> statusFlow, string transitionName)
  {
    var transition = statusFlow.AllTransitions.FirstOrDefault(t => StatusMoveNavBuilder.MatchesTransition(t.Name, transitionName));
    if (transition != null &&
        !StatusMoveNavBuilder.CanMoveOnScreen(
          Feature,
          User,
          transition,
          IsServiceLockedReadOnly,
          elements.StatusFlowAllowsReadOnly,
          ResolveParentChildBuilder()?.IsReadOnly == true,
          typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity))))
    {
      throw RefuseAction(L["MutationRefused"]);
    }
  }

  private async Task<JsonElement?> MergeAskDefaultsAndInputAsync(
    Func<CancellationToken, Task<object?>>? defaultProvider,
    RecordAsk ask,
    JsonElement? askInput,
    CancellationToken ct)
  {
    var defaults = await StatusMoveNavBuilder.ResolveActionDefaultsAsync(defaultProvider, ask, db, PlatformApp, ct);
    if (defaults.Count == 0)
    {
      return askInput;
    }

    if (askInput is { ValueKind: JsonValueKind.Object } obj)
    {
      foreach (var prop in obj.EnumerateObject())
      {
        if (prop.Value.ValueKind != JsonValueKind.Undefined)
        {
          defaults[prop.Name] = prop.Value.Clone();
        }
      }
    }

    return JsonSerializer.SerializeToElement(defaults, PlatformJsonOptions.DefaultOptions);
  }

  private static FormRefusedException RefuseAction(string message, long? recordId = null) =>
    new(MutationOutcome.Refused, message, [new MessageDto { Detail = message, RecordId = recordId, Type = MessageType.Error }]);
}
