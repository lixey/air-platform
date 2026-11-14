using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentProposalBuilder
{
  public static async Task<AgentProposalDto> StageProposalAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct,
    IReadOnlyList<string>? fallbackAttachmentIds = null,
    string? turnId = null)
  {
    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var store = services.GetRequiredService<AgentStore>();
    var options = services.GetService<AgentOptions>() ?? new AgentOptions();
    var bridge = services.GetService<McpExecutionBridge>();
    var owner = AgentStore.ResolveOwner(services);

    var argSessionId = TryGetString(args, "sessionId");
    var sessionId = bridge?.Active is { TurnToken.Length: > 0, SessionId.Length: > 0 } tokenBound
      ? AgentStore.NormalizeSessionId(tokenBound.SessionId)
      : AgentStore.NormalizeSessionId(
        argSessionId
        ?? bridge?.GetContext(argSessionId, owner.TenantId, owner.UserId)?.SessionId
        ?? "default");

    var ctx = new ProposalBuildContext(
      sessionId,
      turnId,
      owner,
      store,
      options,
      services,
      defaultScope.Db,
      defaultScope.App,
      Localizer.Instance);

    var hasNestedValues = args.TryGetProperty("values", out var vProp) && vProp.ValueKind == JsonValueKind.Object;
    var valuesSource = hasNestedValues ? vProp : args;
    var stepKey = AgentStepGraphHelper.TryExtractStepKey(args);
    var recordId = TryGetRecordId(args) ?? (hasNestedValues ? TryGetRecordId(vProp) : null);
    var requestedService = TryGetString(args, "service") ?? TryGetString(args, "kind");
    var requestedOperation = TryGetString(args, "operation") ?? TryGetString(valuesSource, "operation");
    var requestedTransition = TryGetString(args, "transition") ?? TryGetString(valuesSource, "transition");

    if (string.Equals(requestedOperation, AgentProposalOperation.MoveStatus, StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(requestedTransition))
    {
      if (recordId is not > 0 || string.IsNullOrWhiteSpace(requestedTransition))
      {
        throw new InvalidOperationException("Move-status proposals require both 'id' and 'transition'.");
      }

      return await AgentProposalFlowBuilder.StageMoveStatusProposalAsync(
        ctx,
        args,
        requestedTransition.Trim(),
        recordId.Value,
        requestedService,
        ct);
    }

    var descriptors = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App);
    if (descriptors.Count == 0)
    {
      throw new InvalidOperationException("No agent-enabled forms are available for the current user.");
    }

    var isEdit = recordId is > 0;
    var targetDesc = ResolveInitialDescriptor(descriptors, requestedService, ctx);
    if (isEdit)
    {
      targetDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(
        descriptors,
        targetDesc,
        requestedService,
        recordId!.Value,
        services,
        defaultScope.Db,
        defaultScope.App,
        ct);
    }

    await using var formScope = AgentModuleScope.OpenForService(services, targetDesc.FormServiceType, defaultScope.Db, defaultScope.App);
    var formCtx = ctx.WithScope(formScope.Db, formScope.App);
    if (ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, formScope.Db, formScope.App) is not IFormService formInst ||
        !formInst.Feature.AllowsAgentWrite ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, formScope.App))
    {
      throw new InvalidOperationException($"Access denied or form service '{targetDesc.ServicePath}' is unavailable.");
    }

    formInst.SetRequest(new FormRequest { Id = isEdit ? recordId : null });
    var initialDraft = await formInst.PrepareDraftAsync(isEdit ? recordId : null, ct: ct);

    var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
    if (formInst.IsFormReadOnly)
    {
      throw new InvalidOperationException(formCtx.Localizer["FormReadOnly"]);
    }

    var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
    var editableChildren = AgentFeatureRegistry.ResolveEditableChildGrids(children, formInst.Feature, services, formScope.Db, formScope.App);

    var refServices = editableElements
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var childRefSvc in await AgentProposalChildBuilder.CollectChildReferenceServicesAsync(formCtx, editableChildren, isEdit, recordId, targetDesc.ServicePath, ct))
    {
      refServices.Add(childRefSvc);
    }

    var (dictionaries, _) = await AgentContextQueryService.LoadSmartDictionariesAsync(
      refServices, options.MaxFullDictionarySize, [], services, formScope.Db, formScope.App, ct);

    var draft = new ProposalDraftState
    {
      Args = args,
      ValuesSource = valuesSource,
      HasNestedValues = hasNestedValues,
      IsEdit = isEdit,
      RecordId = recordId,
      Target = targetDesc,
      MultiSelectMode = TryGetString(args, "multiSelectMode") ?? TryGetString(valuesSource, "multiSelectMode"),
      FormValues = initialDraft.Values,
      Dictionaries = dictionaries
    };

    await AgentProposalFieldBinder.EnsureExistingReferenceValuesLoadedAsync(formCtx, draft, editableElements, ct);
    await AgentProposalFieldBinder.ParseParentFieldsAsync(formCtx, draft, editableElements, descriptors, ct);

    var pendingRefPaths = draft.NewReferences.Where(nr => nr.LineIndex == null).Select(nr => nr.FieldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    await AgentProposalFieldBinder.ApplyLinkedSelectPatchesAsync(formCtx, draft, editableElements, pendingRefPaths, ct);

    var draftOverrides = draft.ExplicitlyProvided
      .Where(k => !pendingRefPaths.Contains(k))
      .ToDictionary(k => k, k => draft.FormValues.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);
    var preparedDraft = await formInst.PrepareDraftAsync(isEdit ? recordId : null, draftOverrides, pendingRefPaths, ct: ct);

    foreach (var (k, v) in preparedDraft.Values)
    {
      if (!draft.ExplicitlyProvided.Contains(k) && !pendingRefPaths.Contains(k) && v != null)
      {
        draft.FormValues[k] = v;
        var patchedEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, k, StringComparison.OrdinalIgnoreCase));
        if (patchedEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } patchedRefSvc } &&
            TryConvertToLong(v, out var patchedId) && patchedId > 0)
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(patchedRefSvc, patchedId, draft.Dictionaries, formScope.Db, formScope.App, ct);
        }
      }
    }

    var (childDisplayGroups, stagedChildGroups) = await AgentProposalChildBuilder.BuildChildGroupsAsync(
      formCtx, draft, children, editableChildren, ct);

    var orderedNewRefs = await StagePrerequisiteReferencesAsync(formCtx, draft, editableElements, ct);
    var dependsOnIds = CollectDependsOnIds(formCtx, draft, orderedNewRefs);
    var attachmentIds = CollectAttachmentIds(draft, children, fallbackAttachmentIds);

    var displayFields = AgentProposalDisplayBuilder.BuildDisplayFields(
      editableElements,
      initialDraft.OriginalValues,
      draft.FormValues,
      orderedNewRefs,
      draft.ExplicitlyProvided,
      draft.Dictionaries,
      isEdit,
      formCtx.Localizer);

    var baseTitle = AgentProposalDisplayBuilder.BuildProposalTitle(targetDesc, draft.FormValues, orderedNewRefs, editableElements);
    var title = isEdit ? $"#{recordId!.Value} · {baseTitle}" : baseTitle;
    var subtitle = AgentProposalDisplayBuilder.BuildProposalSubtitle(draft.FormValues, orderedNewRefs, editableElements, draft.Dictionaries);
    var operation = isEdit ? AgentProposalOperation.Update : AgentProposalOperation.Create;

    if (AgentDuplicateDetector.FindExistingDuplicateProposal(
          store, sessionId, owner, turnId, stepKey, recordId, operation, targetDesc.ServicePath,
          title, subtitle, attachmentIds, childDisplayGroups, displayFields, WebJson) is { } existingDup)
    {
      return existingDup;
    }

    var (dupWarning, dupRecordId, dupFormService) = isEdit
      ? (null, (long?)null, null)
      : await AgentDuplicateDetector.DetectFileDuplicateAsync(
        formInst,
        targetDesc,
        descriptors,
        children.FirstOrDefault(c => c.IsFiles && c.ServiceType != null)?.ServiceType,
        attachmentIds,
        store,
        owner,
        services,
        formScope.Db,
        formScope.App,
        formCtx.Localizer,
        ct);

    var payloadElement = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Operation = operation,
        Service = targetDesc.ServicePath,
        RecordId = recordId,
        Ver = initialDraft.Ver,
        StepKey = stepKey,
        Values = draft.FormValues,
        ExplicitFields = [.. draft.ExplicitlyProvided],
        DependsOnProposalIds = dependsOnIds,
        NewReferences = orderedNewRefs,
        Children = stagedChildGroups
      },
      WebJson);

    var proposal = new AgentProposalDto
    {
      Id = Guid.NewGuid().ToString("N")[..12],
      SessionId = sessionId,
      Operation = operation,
      Service = targetDesc.ServicePath,
      RecordId = recordId,
      StepKey = stepKey,
      DependsOn = dependsOnIds.Count > 0 ? dependsOnIds : null,
      Title = title,
      Subtitle = subtitle,
      Badge = isEdit ? formCtx.Localizer["AgentEditBadge"] : targetDesc.Label,
      BadgeColor = isEdit ? UiColor.Amber : (targetDesc.Color ?? UiColor.Blue),
      Fields = displayFields,
      Children = childDisplayGroups,
      AttachmentIds = attachmentIds,
      Payload = payloadElement,
      FormService = targetDesc.ServicePath,
      Overlay = await formInst.OverlayAsync(targetDesc.ServicePath, ct),
      DuplicateWarning = dupWarning,
      DuplicateRecordId = dupRecordId,
      DuplicateFormService = dupFormService,
      TurnId = turnId
    };

    store.StageProposal(proposal, owner);
    return proposal;
  }

  private static async Task<List<PendingNewReference>> StagePrerequisiteReferencesAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    IReadOnlyList<FormElementDto> editableElements,
    CancellationToken ct)
  {
    var ordered = draft.NewReferences
      .OrderBy(nr => nr.LineIndex.HasValue ? 1 : 0)
      .ThenBy(nr => nr.LineIndex ?? editableElements.FirstOrDefault(e => string.Equals(e.Path, nr.FieldPath, StringComparison.OrdinalIgnoreCase))?.SelectPatches?.Count ?? 0)
      .ToList();

    foreach (var pendingRef in ordered)
    {
      if (pendingRef.LineIndex != null || !string.IsNullOrEmpty(pendingRef.ProposalId) || string.IsNullOrEmpty(pendingRef.FormService))
      {
        continue;
      }

      var sourceEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, pendingRef.FieldPath, StringComparison.OrdinalIgnoreCase));
      if (sourceEl?.SelectPatches is { Count: > 0 } patches)
      {
        foreach (var patch in patches)
        {
          var sourceIdKey = $"{patch.SourceColumn}Id";
          if (draft.FormValues.TryGetValue(patch.TargetField, out var targetVal) && TryConvertToLong(targetVal, out var targetId) && targetId > 0)
          {
            pendingRef.ExtraValues.TryAdd(sourceIdKey, targetId);
            pendingRef.ExtraValues.TryAdd(patch.SourceColumn, targetId);
          }
          else if (draft.NewReferences.FirstOrDefault(nr => nr.LineIndex == null && string.Equals(nr.FieldPath, patch.TargetField, StringComparison.OrdinalIgnoreCase)) is { } tpr)
          {
            pendingRef.ExtraValues.TryAdd(sourceIdKey, tpr.Name);
            pendingRef.ExtraValues.TryAdd(patch.SourceColumn, tpr.Name);
          }
        }
      }

      if (sourceEl?.Model is FormSelectModel { ReferenceDraftRules: { Count: > 0 } draftRules })
      {
        foreach (var rule in draftRules)
        {
          if ((!draft.IsEdit || draft.ExplicitlyProvided.Contains(rule.DraftField)) &&
              draft.FormValues.TryGetValue(rule.DraftField, out var draftVal) && draftVal != null)
          {
            pendingRef.ExtraValues.TryAdd(rule.SourceField, draftVal);
          }
        }
      }

      var existingPrereq = ctx.Store.GetSessionProposals(ctx.SessionId, ctx.Owner).FirstOrDefault(p =>
        p.Status == "pending" &&
        (ctx.TurnId == null || string.Equals(p.TurnId, ctx.TurnId, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(p.Service, pendingRef.FormService, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(AgentStepGraphHelper.GetProposalShortLabel(p), pendingRef.Name, StringComparison.OrdinalIgnoreCase));

      if (existingPrereq != null)
      {
        pendingRef.ProposalId = existingPrereq.Id;
        pendingRef.StepNumber ??= existingPrereq.StepNumber;
        pendingRef.StepKey ??= existingPrereq.StepKey;
        continue;
      }

      var primaryTextKey = "name";
      var refFormType = ModuleCatalog.Instance.FindServiceType(pendingRef.FormService);
      if (refFormType != null)
      {
        await using var refScope = AgentModuleScope.OpenForService(ctx.Services, refFormType, ctx.Db, ctx.App);
        if (ModuleCatalog.Instance.CreateServiceInstance(refFormType, refScope.Db, refScope.App) is IFormService refFormInst)
        {
          var (refEls, _) = await refFormInst.GetElementsMetadataAsync(ct);
          primaryTextKey = ResolvePrimaryTextFieldPath(refEls, (sourceEl?.Model as FormSelectModel)?.ReferenceLabelPaths);
        }
      }

      var prereqValues = new Dictionary<string, object?>(pendingRef.ExtraValues, StringComparer.OrdinalIgnoreCase) { [primaryTextKey] = pendingRef.Name };
      var prereqArgs = JsonSerializer.SerializeToElement(new { sessionId = ctx.SessionId, service = pendingRef.FormService, values = prereqValues }, WebJson);
      try
      {
        var stagedPrereq = await StageProposalAsync(prereqArgs, ctx.Services, ct, turnId: ctx.TurnId);
        pendingRef.ProposalId = stagedPrereq.Id;
        pendingRef.StepNumber = stagedPrereq.StepNumber;
      }
      catch { }
    }

    return ordered;
  }

  private static List<string> CollectDependsOnIds(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    IReadOnlyList<PendingNewReference> orderedNewRefs)
  {
    var dependsOnIds = orderedNewRefs
      .Select(nr => nr.ProposalId)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (draft.TryReadEnvelopeProperty("dependsOn", out var depProp) && depProp.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in depProp.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String && AgentStepGraphHelper.NormalizeStepKey(item.GetString()) is { } depStepKey)
        {
          var depTarget = ctx.FindReferencedProposal(depStepKey)
            ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
          if (!dependsOnIds.Contains(depTarget.Id, StringComparer.OrdinalIgnoreCase))
          {
            dependsOnIds.Add(depTarget.Id);
          }
        }
      }
    }

    return dependsOnIds;
  }

  private static List<string> CollectAttachmentIds(
    ProposalDraftState draft,
    IReadOnlyList<ChildGridBuilder> children,
    IReadOnlyList<string>? fallbackAttachmentIds)
  {
    var attachmentIds = new List<string>();
    if (!children.Any(c => c.IsFiles))
    {
      return attachmentIds;
    }

    if (draft.TryReadEnvelopeProperty("attachmentIds", out var attProp) && attProp.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in attProp.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } aid)
        {
          attachmentIds.Add(aid);
        }
      }
    }

    if (attachmentIds.Count == 0 && fallbackAttachmentIds is { Count: > 0 })
    {
      attachmentIds.AddRange(fallbackAttachmentIds);
    }

    return attachmentIds;
  }

  private static GridCreateDescriptor ResolveInitialDescriptor(
    IReadOnlyList<GridCreateDescriptor> descriptors,
    string? requestedService,
    ProposalBuildContext ctx)
  {
    if (!string.IsNullOrWhiteSpace(requestedService))
    {
      return AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, ctx.Services, ctx.Db, ctx.App)
        ?? throw new InvalidOperationException($"Form service '{requestedService}' is not available or not enabled for AI mutations.");
    }

    throw new InvalidOperationException("Proposal must specify a valid 'service' from available forms.");
  }
}
