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
    var L = Localizer.Instance;
    var owner = AgentStore.ResolveOwner(services);

    var argSessionId = TryGetString(args, "sessionId");
    var sessionId = bridge?.Active is { TurnToken.Length: > 0, SessionId.Length: > 0 } tokenBound
      ? AgentStore.NormalizeSessionId(tokenBound.SessionId)
      : AgentStore.NormalizeSessionId(
        argSessionId
        ?? bridge?.GetContext(argSessionId, owner.TenantId, owner.UserId)?.SessionId
        ?? "default");

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
        args,
        valuesSource,
        requestedTransition.Trim(),
        recordId.Value,
        requestedService,
        sessionId,
        stepKey,
        turnId,
        owner,
        store,
        options,
        (rk, refSvc) => AgentDuplicateDetector.FindReferencedProposal(store, sessionId, owner, turnId, rk, refSvc),
        services,
        defaultScope.Db,
        defaultScope.App,
        L,
        ct);
    }

    var descriptors = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App);
    if (descriptors.Count == 0)
    {
      throw new InvalidOperationException("No agent-enabled forms are available for the current user.");
    }

    var isEdit = recordId is > 0;
    var targetDesc = ResolveInitialDescriptor(descriptors, requestedService, services, defaultScope.Db, defaultScope.App);
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
    if (ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, formScope.Db, formScope.App) is not IFormService formInst ||
        !formInst.Feature.AllowsAgentWrite ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, formScope.App))
    {
      throw new InvalidOperationException($"Access denied or form service '{targetDesc.ServicePath}' is unavailable.");
    }

    formInst.SetRequest(new FormRequest { Id = isEdit ? recordId : null });
    var initialDraft = await formInst.PrepareDraftAsync(isEdit ? recordId : null, ct: ct);
    var originalFormValues = initialDraft.OriginalValues;
    var formValues = initialDraft.Values;
    var recordVer = initialDraft.Ver;

    var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
    var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
    var editableChildren = AgentFeatureRegistry.ResolveEditableChildGrids(children, formInst.Feature, services, formScope.Db, formScope.App);

    var refServices = editableElements
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var childRefSvc in AgentProposalChildBuilder.CollectChildReferenceServices(editableChildren, isEdit, recordId, targetDesc.ServicePath, services, formScope.Db, formScope.App))
    {
      refServices.Add(childRefSvc);
    }

    var (dictionaries, _) = await AgentContextQueryService.LoadSmartDictionariesAsync(
      refServices, options.MaxFullDictionarySize, [], services, formScope.Db, formScope.App, ct);

    await AgentProposalFieldBinder.EnsureExistingReferenceValuesLoadedAsync(editableElements, formValues, dictionaries, formScope.Db, formScope.App, ct);

    var multiSelectMode = TryGetString(args, "multiSelectMode") ?? TryGetString(valuesSource, "multiSelectMode");
    var newReferences = new List<PendingNewReference>();
    var explicitlyProvided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var linkedSelectPatches = new List<(FormElementDto Element, AgentProposalDto TargetProposal)>();

    await AgentProposalFieldBinder.ParseParentFieldsAsync(
      args, valuesSource, hasNestedValues, isEdit, multiSelectMode, editableElements, descriptors,
      formValues, explicitlyProvided, newReferences, linkedSelectPatches, dictionaries,
      (rk, refSvc) => AgentDuplicateDetector.FindReferencedProposal(store, sessionId, owner, turnId, rk, refSvc),
      services, formScope.Db, formScope.App, L, ct);

    var pendingRefPaths = newReferences.Where(nr => nr.LineIndex == null).Select(nr => nr.FieldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    await AgentProposalFieldBinder.ApplyLinkedSelectPatchesAsync(
      linkedSelectPatches, editableElements, formValues, explicitlyProvided, pendingRefPaths, dictionaries, formScope.Db, formScope.App, ct);

    var draftOverrides = explicitlyProvided
      .Where(k => !pendingRefPaths.Contains(k))
      .ToDictionary(k => k, k => formValues.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);
    var preparedDraft = await formInst.PrepareDraftAsync(isEdit ? recordId : null, draftOverrides, pendingRefPaths, ct: ct);

    foreach (var (k, v) in preparedDraft.Values)
    {
      if (!explicitlyProvided.Contains(k) && !pendingRefPaths.Contains(k) && v != null)
      {
        formValues[k] = v;
        var patchedEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, k, StringComparison.OrdinalIgnoreCase));
        if (patchedEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } patchedRefSvc } &&
            TryConvertToLong(v, out var patchedId) && patchedId > 0)
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(patchedRefSvc, patchedId, dictionaries, formScope.Db, formScope.App, ct);
        }
      }
    }

    var (childDisplayGroups, stagedChildGroups) = await AgentProposalChildBuilder.BuildChildGroupsAsync(
      args, valuesSource, children, editableChildren, formInst.Feature, isEdit, recordId, targetDesc,
      formValues, explicitlyProvided, newReferences, dictionaries,
      (rk, refSvc) => AgentDuplicateDetector.FindReferencedProposal(store, sessionId, owner, turnId, rk, refSvc),
      services, formScope.Db, formScope.App, L, ct);

    var orderedNewRefs = await StagePrerequisiteReferencesAsync(
      newReferences, editableElements, formValues, explicitlyProvided, isEdit, store, sessionId, owner, turnId, services, formScope.Db, formScope.App, ct);

    var dependsOnIds = CollectDependsOnIds(args, hasNestedValues ? vProp : default, orderedNewRefs, store, sessionId, owner, turnId, L);
    var attachmentIds = CollectAttachmentIds(args, valuesSource, children, fallbackAttachmentIds);

    var displayFields = AgentProposalDisplayBuilder.BuildDisplayFields(
      editableElements, originalFormValues, formValues, orderedNewRefs, explicitlyProvided, dictionaries, isEdit, L);

    var baseTitle = AgentProposalDisplayBuilder.BuildProposalTitle(targetDesc, formValues, orderedNewRefs, editableElements);
    var title = isEdit ? $"#{recordId!.Value} · {baseTitle}" : baseTitle;
    var subtitle = AgentProposalDisplayBuilder.BuildProposalSubtitle(formValues, orderedNewRefs, editableElements, dictionaries);
    var operation = isEdit ? AgentProposalOperation.Update : AgentProposalOperation.Create;

    if (AgentDuplicateDetector.FindExistingDuplicateProposal(store, sessionId, owner, turnId, stepKey, recordId, operation, targetDesc.ServicePath, title, subtitle, attachmentIds, childDisplayGroups, displayFields, WebJson) is { } existingDup)
    {
      return existingDup;
    }

    var (dupWarning, dupRecordId, dupFormService) = isEdit
      ? (null, (long?)null, null)
      : await AgentDuplicateDetector.DetectFileDuplicateAsync(
        formInst, targetDesc, descriptors,
        children.FirstOrDefault(c => c.IsFiles && c.ServiceType != null)?.ServiceType,
        attachmentIds, store, owner, services, formScope.Db, formScope.App, L, ct);

    var payloadElement = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Operation = operation,
        Service = targetDesc.ServicePath,
        RecordId = recordId,
        Ver = recordVer,
        StepKey = stepKey,
        Values = formValues,
        ExplicitFields = [.. explicitlyProvided],
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
      Badge = isEdit ? L["AgentEditBadge"] : targetDesc.Label,
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
    List<PendingNewReference> newReferences,
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitlyProvided,
    bool isEdit,
    AgentStore store,
    string sessionId,
    AgentOwnerScope owner,
    string? turnId,
    IServiceProvider services,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var ordered = newReferences
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
          if (formValues.TryGetValue(patch.TargetField, out var targetVal) && TryConvertToLong(targetVal, out var targetId) && targetId > 0)
          {
            pendingRef.ExtraValues.TryAdd(sourceIdKey, targetId);
            pendingRef.ExtraValues.TryAdd(patch.SourceColumn, targetId);
          }
          else if (newReferences.FirstOrDefault(nr => nr.LineIndex == null && string.Equals(nr.FieldPath, patch.TargetField, StringComparison.OrdinalIgnoreCase)) is { } tpr)
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
          if ((!isEdit || explicitlyProvided.Contains(rule.DraftField)) &&
              formValues.TryGetValue(rule.DraftField, out var draftVal) && draftVal != null)
          {
            pendingRef.ExtraValues.TryAdd(rule.SourceField, draftVal);
          }
        }
      }

      var existingPrereq = store.GetSessionProposals(sessionId, owner).FirstOrDefault(p =>
        p.Status == "pending" &&
        (turnId == null || string.Equals(p.TurnId, turnId, StringComparison.OrdinalIgnoreCase)) &&
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
        await using var refScope = AgentModuleScope.OpenForService(services, refFormType, db, app);
        if (ModuleCatalog.Instance.CreateServiceInstance(refFormType, refScope.Db, refScope.App) is IFormService refFormInst)
        {
          var (refEls, _) = await refFormInst.GetElementsMetadataAsync(ct);
          primaryTextKey = ResolvePrimaryTextFieldPath(refEls, (sourceEl?.Model as FormSelectModel)?.ReferenceLabelPaths);
        }
      }

      var prereqValues = new Dictionary<string, object?>(pendingRef.ExtraValues, StringComparer.OrdinalIgnoreCase) { [primaryTextKey] = pendingRef.Name };
      var prereqArgs = JsonSerializer.SerializeToElement(new { sessionId, service = pendingRef.FormService, values = prereqValues }, WebJson);
      try
      {
        var stagedPrereq = await StageProposalAsync(prereqArgs, services, ct, turnId: turnId);
        pendingRef.ProposalId = stagedPrereq.Id;
        pendingRef.StepNumber = stagedPrereq.StepNumber;
      }
      catch { }
    }

    return ordered;
  }

  private static List<string> CollectDependsOnIds(
    JsonElement args,
    JsonElement vProp,
    IReadOnlyList<PendingNewReference> orderedNewRefs,
    AgentStore store,
    string sessionId,
    AgentOwnerScope owner,
    string? turnId,
    Localizer L)
  {
    var dependsOnIds = orderedNewRefs
      .Select(nr => nr.ProposalId)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if ((TryGetPropertyIgnoreCase(args, "dependsOn", out var depProp) ||
         vProp.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(vProp, "dependsOn", out depProp)) &&
        depProp.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in depProp.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String && AgentStepGraphHelper.NormalizeStepKey(item.GetString()) is { } depStepKey)
        {
          var depTarget = AgentDuplicateDetector.FindReferencedProposal(store, sessionId, owner, turnId, depStepKey, null)
            ?? throw new InvalidOperationException(L["MutationRefused"]);
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
    JsonElement args,
    JsonElement valuesSource,
    IReadOnlyList<ChildGridBuilder> children,
    IReadOnlyList<string>? fallbackAttachmentIds)
  {
    var attachmentIds = new List<string>();
    if (!children.Any(c => c.IsFiles))
    {
      return attachmentIds;
    }

    if ((TryGetPropertyIgnoreCase(args, "attachmentIds", out var attProp) ||
         TryGetPropertyIgnoreCase(valuesSource, "attachmentIds", out attProp)) &&
        attProp.ValueKind == JsonValueKind.Array)
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
    IServiceProvider services,
    PlatformDbContext db,
    PlatformAppService app)
  {
    if (!string.IsNullOrWhiteSpace(requestedService))
    {
      return AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, services, db, app)
        ?? throw new InvalidOperationException($"Form service '{requestedService}' is not available or not enabled for AI mutations.");
    }

    throw new InvalidOperationException("Proposal must specify a valid 'service' from available forms.");
  }
}
