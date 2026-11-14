using Platform.App.Screens.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  public virtual async Task<FlowDescriptionDto?> DescribeStatusFlowAsync(CancellationToken ct = default)
  {
    var cols = await CreateColumnsAsync(ct);
    if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      return null;
    }

    (statusFlow as IPlatformService)?.BindContext(PlatformApp);
    var desc = await statusFlow.DescribeFlowAsync(ct);
    if (typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity)) ||
        IsServiceLockedReadOnly && !cols.StatusFlowAllowsReadOnly)
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
    var cols = await CreateColumnsAsync(ct);
    if (cols.StatusFlow is not IRecordStatuses<TEntity> statusFlow)
    {
      throw new InvalidOperationException($"Grid '{GetType().Name}' does not use a status flow.");
    }

    (statusFlow as IPlatformService)?.BindContext(PlatformApp);

    if (id is > 0)
    {
      await InferBoundParentIdFromIdsAsync([id.Value], ct);
      if (!await EnsureParentScopeAsync(lockParent: false, ct))
      {
        throw new KeyNotFoundException(L["FormNotFound"]);
      }

      EnsureGridScreenAllowsMove(cols, statusFlow, transitionName);

      var entity = await RestrictedQuery()
        .AsNoTracking()
        .FirstOrDefaultAsync(e => e.Id == id.Value, ct)
        ?? throw new KeyNotFoundException(L["FormNotFound"]);

      var preview = await statusFlow.PreviewMoveAsync(entity, transitionName, askInput, skipValidationPaths, ct);
      var caption = await GetRecordLabelAsync(entity, ct);
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

    EnsureGridScreenAllowsMove(cols, statusFlow, transitionName);
    var synthetic = statusFlow.Initialize(new TEntity());
    return await statusFlow.PreviewMoveAsync(synthetic, transitionName, askInput, skipValidationPaths, ct);
  }

  public virtual async Task<IReadOnlyList<DeclaredActionDescriptorDto>> DescribeDeclaredActionsAsync(
    bool automationOnly = false,
    CancellationToken ct = default)
  {
    var actions = await BuildActionsAsync(ct);
    var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
    var servicePath = ServicePathResolver.Resolve(GetType());
    var descriptors = new List<DeclaredActionDescriptorDto>();
    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
      seenHandles.Add(entry.Handle);
      descriptors.Add(new DeclaredActionDescriptorDto
      {
        Service = servicePath,
        Handle = entry.Handle,
        Method = entry.Method,
        Label = L[rawLabel],
        Title = entry.TitleText != null ? L[entry.TitleText] : null,
        Confirm = !string.IsNullOrEmpty(entry.ConfirmMessage) ? L[entry.ConfirmMessage] : null,
        Color = entry.ColorHandle ?? UiColor.Blue,
        SelectionMode = ResolveSelectionMode(entry),
        IsAutomationAllowed = entry.IsAgentAllowed,
        AskElements = askElements
      });
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form)
      {
        var inherited = await form.DescribeDeclaredActionsAsync(automationOnly, ct);
        foreach (var item in inherited)
        {
          if (seenHandles.Add(item.Handle) &&
              await form.IsActionPermittedForGridAsync(item.Handle, IsReadOnly, isParentReadOnly, ct))
          {
            descriptors.Add(new DeclaredActionDescriptorDto
            {
              Service = servicePath,
              Handle = item.Handle,
              Method = item.Method,
              Label = item.Label,
              Title = item.Title,
              Confirm = item.Confirm,
              Color = item.Color,
              SelectionMode = "single",
              IsAutomationAllowed = item.IsAutomationAllowed,
              AskElements = item.AskElements
            });
          }
        }
      }
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
    var actions = await BuildActionsAsync(ct, bypassCache: true);
    var entry = actions.Entries.FirstOrDefault(e => e.Matches(actionKey));
    var servicePath = ServicePathResolver.Resolve(GetType());

    if (entry != null)
    {
      if (requireAutomationOptIn && !entry.IsAgentAllowed)
      {
        throw RefuseGridAction(L["MutationRefused"]);
      }

      var normalizedIds = entry.IgnoresSelection ? [] : selectedIds.Distinct().ToList();
      if (entry.NeedsSelection && normalizedIds.Count == 0 ||
          entry.IsSingle && normalizedIds.Count != 1)
      {
        throw RefuseGridAction(L["NoRecordsSelected"]);
      }

      var persistedIds = normalizedIds.Where(id => id > 0).ToList();
      if (persistedIds.Count > 0)
      {
        await InferBoundParentIdFromIdsAsync(persistedIds, ct);
        if (!await EnsureParentScopeAsync(lockParent: false, ct))
        {
          throw new KeyNotFoundException(L["FormNotFound"]);
        }
      }

      SelectedIds = persistedIds;
      var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
      if (!entry.Permission.IsPermitted(Feature, User, entry.Handle, IsReadOnly, entry.IsAllowedInReadOnly, isParentReadOnly))
      {
        throw RefuseGridAction(L["MutationRefused"]);
      }

      List<TEntity> records = [];
      List<string> captions = [];
      if (persistedIds.Count > 0)
      {
        var loaded = await RestrictedQuery()
          .AsNoTracking()
          .Where(e => persistedIds.Contains(e.Id))
          .ToListAsync(ct);
        var byId = loaded.ToDictionary(e => e.Id);
        foreach (var pid in persistedIds)
        {
          if (!byId.TryGetValue(pid, out var rec))
          {
            throw new KeyNotFoundException(L["FormNotFound"]);
          }

          records.Add(rec);
          if (await GetRecordLabelAsync(rec, ct) is { Length: > 0 } label)
          {
            captions.Add(label);
          }
        }

        if (entry.Offers.Count > 0)
        {
          foreach (var offer in entry.Offers)
          {
            if (offer.Condition is Expression<Func<TEntity, bool>> cond)
            {
              var passedIds = (await db.Set<TEntity>()
                .Where(e => persistedIds.Contains(e.Id))
                .Where(cond)
                .Select(e => e.Id)
                .ToListAsync(ct)).ToHashSet();

              foreach (var rec in records)
              {
                if (!passedIds.Contains(rec.Id))
                {
                  throw RefuseGridAction(L[offer.Reason], rec.Id);
                }
              }
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
          ? (Func<CancellationToken, Task<object?>>)(token => entry.DefaultProvider(persistedIds, token))
          : null;
        var mergedAskJson = await MergeGridAskDefaultsAndInputAsync(defaultProvider, entry.Ask, askInput, ct);
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
      if (records.Count > 0 && entry.Guards.Count > 0)
      {
        var ctx = new GridBatchContext<TEntity>(persistedIds, records, _ => Task.FromResult(0), db);
        foreach (var (guard, refusal, path) in entry.Guards)
        {
          if (path != null && skipValidationPaths?.Contains(path) == true)
          {
            continue;
          }

          var errorsBefore = ctx.Errors.Count;
          var guardPassed = await guard(ctx, records, boundInput, L, ct);
          if (!guardPassed && ctx.Errors.Count == errorsBefore)
          {
            ctx.AddError("grid.guard", L[refusal], records[0].Id, path);
          }

          if (!guardPassed || ctx.Errors.Count > errorsBefore)
          {
            var errors = FormatErrors(ctx.Errors, records, MessageType.Error);
            throw new FormRefusedException(MutationOutcome.Refused, errors[0].Detail ?? L[refusal], errors);
          }
        }

        if (ctx.Warnings.Count > 0)
        {
          warnings.AddRange(FormatErrors(ctx.Warnings, records, MessageType.Warning));
        }
      }

      var rawLabel = entry.LabelText ?? Grid.Columns.PathToLabel(entry.Handle);
      var versions = records.Count > 0 && records.Count == normalizedIds.Count && typeof(IHasVersion).IsAssignableFrom(typeof(TEntity))
        ? records.Select(r => ((IHasVersion)r).Ver).ToList()
        : null;

      return new DeclaredActionPreviewDto
      {
        Service = servicePath,
        Handle = entry.Handle,
        Method = entry.Method,
        Label = L[rawLabel],
        Color = entry.ColorHandle ?? UiColor.Blue,
        SelectionMode = ResolveSelectionMode(entry),
        TargetIds = normalizedIds,
        TargetVersions = versions,
        TargetCaptions = captions,
        AskElements = askElements,
        AskValues = askValues,
        Warnings = warnings
      };
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is IFormService form &&
          await form.HasActionAsync(actionKey, ct))
      {
        if (selectedIds.Count == 1 && selectedIds[0] > 0)
        {
          var reachable = await RestrictedQuery().AsNoTracking().AnyAsync(e => e.Id == selectedIds[0], ct);
          if (!reachable)
          {
            throw new KeyNotFoundException(L["FormNotFound"]);
          }
        }

        var isParentReadOnly = ResolveParentChildBuilder()?.IsReadOnly == true;
        if (!await form.IsActionPermittedForGridAsync(actionKey, IsReadOnly, isParentReadOnly, ct))
        {
          throw RefuseGridAction(L["MutationRefused"]);
        }

        var formPreview = await form.PreviewDeclaredActionAsync(actionKey, selectedIds, askInput, skipValidationPaths, requireAutomationOptIn, ct);
        if (formPreview != null)
        {
          return new DeclaredActionPreviewDto
          {
            Service = servicePath,
            Handle = formPreview.Handle,
            Method = formPreview.Method,
            Label = formPreview.Label,
            Color = formPreview.Color,
            SelectionMode = formPreview.SelectionMode,
            TargetIds = formPreview.TargetIds,
            TargetVersions = formPreview.TargetVersions,
            TargetCaptions = formPreview.TargetCaptions,
            AskElements = formPreview.AskElements,
            AskValues = formPreview.AskValues,
            Warnings = formPreview.Warnings
          };
        }
      }
    }

    return null;
  }

  private static string ResolveSelectionMode(GridActionItem<TEntity> entry)
  {
    if (entry.IgnoresSelection)
    {
      return "none";
    }

    if (entry.IsSingle)
    {
      return "single";
    }

    return entry.NeedsSelection ? "multi" : "optional";
  }

  private void EnsureGridScreenAllowsMove(Columns<TEntity, TRow> cols, IRecordStatuses<TEntity> statusFlow, string transitionName)
  {
    var transition = statusFlow.AllTransitions.FirstOrDefault(t => StatusMoveNavBuilder.MatchesTransition(t.Name, transitionName));
    if (transition != null &&
        !StatusMoveNavBuilder.CanMoveOnScreen(
          Feature,
          User,
          transition,
          IsServiceLockedReadOnly,
          cols.StatusFlowAllowsReadOnly,
          ResolveParentChildBuilder()?.IsReadOnly == true,
          typeof(IReadOnlyEntity).IsAssignableFrom(typeof(TEntity))))
    {
      throw RefuseGridAction(L["MutationRefused"]);
    }
  }

  private async Task<JsonElement?> MergeGridAskDefaultsAndInputAsync(
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

  private static FormRefusedException RefuseGridAction(string message, long? recordId = null) =>
    new(MutationOutcome.Refused, message, [new MessageDto { Detail = message, RecordId = recordId, Type = MessageType.Error }]);
}
