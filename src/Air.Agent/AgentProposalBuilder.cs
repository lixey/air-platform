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
      sessionId, turnId, owner, store, options, services, defaultScope.Db, defaultScope.App, Localizer.Instance);

    var hasNestedValues = TryGetPropertyIgnoreCase(args, "values", out var vProp) && vProp.ValueKind == JsonValueKind.Object;
    var valuesSource = hasNestedValues ? vProp : args;
    var stepKey = AgentStepGraphHelper.TryExtractStepKey(args);
    var targetRefKey = TryExtractTargetRefKey(args, valuesSource);
    long? recordId = null;
    if (targetRefKey == null)
    {
      recordId = TryGetRecordId(args) ?? (hasNestedValues ? TryGetRecordId(vProp) : null);
    }

    var requestedService = TryGetString(args, "service") ?? TryGetString(args, "kind");
    var requestedOperation = TryGetString(args, "operation") ?? TryGetString(valuesSource, "operation");
    var requestedTransition = TryGetString(args, "transition") ?? TryGetString(valuesSource, "transition");
    var requestedAction = TryGetString(args, "action") ?? TryGetString(valuesSource, "action");

    if (string.IsNullOrWhiteSpace(requestedOperation) && IsLegacyOperationAlias(requestedAction))
    {
      requestedOperation = requestedAction;
      requestedAction = null;
    }

    if (string.Equals(requestedOperation, AgentProposalOperation.MoveStatus, StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(requestedTransition))
    {
      if (recordId is not > 0 && string.IsNullOrWhiteSpace(targetRefKey) || string.IsNullOrWhiteSpace(requestedTransition))
      {
        throw new InvalidOperationException("Move-status proposals require both 'id' and 'transition'.");
      }

      return await AgentProposalFlowBuilder.StageMoveStatusProposalAsync(
        ctx, args, requestedTransition.Trim(), recordId, targetRefKey, requestedService, ct);
    }

    if (string.Equals(requestedOperation, AgentProposalOperation.RunAction, StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(requestedOperation) && !string.IsNullOrWhiteSpace(requestedAction))
    {
      if (string.IsNullOrWhiteSpace(requestedAction))
      {
        throw new InvalidOperationException("Run-action proposals require 'action'.");
      }

      return await AgentProposalActionBuilder.StageRunActionProposalAsync(
        ctx, args, requestedAction.Trim(), requestedService, ct);
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
        descriptors, targetDesc, requestedService, recordId!.Value, services, defaultScope.Db, defaultScope.App, ct);
    }

    await using var formScope = AgentModuleScope.OpenForService(services, targetDesc.FormServiceType, defaultScope.Db, defaultScope.App);
    var formCtx = ctx.WithScope(formScope.Db, formScope.App);
    if (ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, formScope.Db, formScope.App) is not IFormService formInst ||
        !formInst.Feature.AllowsAgentWrite ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, formScope.App))
    {
      throw new InvalidOperationException($"Access denied or form service '{targetDesc.ServicePath}' is unavailable.");
    }

    var multiSelectMode = TryGetString(args, "multiSelectMode") ?? TryGetString(valuesSource, "multiSelectMode") ?? "replace";
    FormCompositeDraftDto draft;
    try
    {
      draft = await formInst.PrepareCompositeDraftAsync(
        new FormCompositeDraftRequest
        {
          RecordId = isEdit ? recordId : null,
          Args = args,
          Values = hasNestedValues ? vProp : null,
          MultiSelectMode = multiSelectMode,
          MaxPreloadDictionarySize = options.MaxFullDictionarySize,
          ChildPermissionFilter = (childFeat, parentFeat) =>
            !childFeat.IsReadOnly &&
            AgentFeatureRegistry.IsChildAgentWriteAllowed(childFeat, parentFeat) &&
            AgentFeatureRegistry.CanViewFeature(childFeat, formScope.App) &&
            AgentFeatureRegistry.CanEditFeature(childFeat, formScope.App),
          CreateFormResolver = (el, refSvc) =>
            AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, refSvc, el, services, formScope.Db, formScope.App)?.ServicePath
        },
        ct);
    }
    catch (FormRefusedException refusedEx)
    {
      var (errEls, _) = await formInst.GetElementsMetadataAsync(ct);
      throw new InvalidOperationException(FormatRefusedException(refusedEx, errEls), refusedEx);
    }

    var editableElements = draft.Elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
    var (_, declaredChildren) = await formInst.GetElementsMetadataAsync(ct);

    var orderedNewRefs = await AgentPrerequisiteReferenceStager.ResolveAndStageReferencesAsync(
      formCtx, formInst, isEdit, recordId, draft, editableElements, ct);

    var firstError = draft.Messages.FirstOrDefault(m => string.Equals(m.Type, MessageType.Error, StringComparison.OrdinalIgnoreCase));
    if (firstError != null)
    {
      var detail = firstError.Detail ?? firstError.Title ?? formCtx.Localizer["MutationRefused"];
      if (!string.IsNullOrEmpty(firstError.Path))
      {
        var fieldLabel = editableElements.FirstOrDefault(e => string.Equals(e.Path, firstError.Path, StringComparison.OrdinalIgnoreCase))?.Label
          ?? firstError.Path;
        if (!detail.StartsWith(fieldLabel, StringComparison.OrdinalIgnoreCase))
        {
          detail = $"{fieldLabel}: {detail}";
        }
      }

      throw new InvalidOperationException(detail);
    }

    var warnings = draft.Messages
      .Where(m => string.Equals(m.Type, MessageType.Warning, StringComparison.OrdinalIgnoreCase))
      .Select(m => m.Detail ?? m.Title)
      .Where(s => !string.IsNullOrWhiteSpace(s))
      .Select(s => s!)
      .Distinct(StringComparer.Ordinal)
      .ToList();

    var (childDisplayGroups, stagedChildGroups) = AgentProposalDisplayBuilder.BuildChildGroupsFromDraft(
      draft.Children, orderedNewRefs, draft.Dictionaries, formCtx.Localizer);

    var dependsOnIds = CollectDependsOnIds(formCtx, args, valuesSource, orderedNewRefs);
    var attachmentIds = CollectAttachmentIds(args, valuesSource, declaredChildren, fallbackAttachmentIds);

    var displayFields = AgentProposalDisplayBuilder.BuildDisplayFields(
      editableElements, draft.OriginalValues, draft.Values, orderedNewRefs, draft.ExplicitFields, draft.Dictionaries, isEdit, formCtx.Localizer);

    var baseTitle = AgentProposalDisplayBuilder.BuildProposalTitle(targetDesc, draft.Values, orderedNewRefs, editableElements);
    var title = isEdit ? $"#{recordId!.Value} · {baseTitle}" : baseTitle;
    var subtitle = AgentProposalDisplayBuilder.BuildProposalSubtitle(draft.Values, orderedNewRefs, editableElements, draft.Dictionaries);
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
        formInst, targetDesc, descriptors,
        declaredChildren.FirstOrDefault(c => c.IsFiles && c.ServiceType != null)?.ServiceType,
        attachmentIds, store, owner, services, formScope.Db, formScope.App, formCtx.Localizer, ct);

    var payloadElement = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Operation = operation,
        Service = targetDesc.ServicePath,
        RecordId = recordId,
        Ver = draft.Ver,
        StepKey = stepKey,
        Values = draft.Values,
        ExplicitFields = [.. draft.ExplicitFields],
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
      Warnings = warnings.Count > 0 ? warnings : null,
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

  private static string? TryExtractTargetRefKey(JsonElement args, JsonElement valuesSource)
  {
    foreach (var idProp in new[] { "id", "recordId" })
    {
      if ((TryGetPropertyIgnoreCase(args, idProp, out var rawEl) ||
           !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, idProp, out rawEl)) &&
          AgentStepGraphHelper.TryExtractRefKey(rawEl, out var refKey))
      {
        return refKey;
      }
    }

    return null;
  }

  private static bool IsLegacyOperationAlias(string? action) =>
    !string.IsNullOrWhiteSpace(action) &&
    (string.Equals(action, AgentProposalOperation.Create, StringComparison.OrdinalIgnoreCase) ||
     string.Equals(action, AgentProposalOperation.Update, StringComparison.OrdinalIgnoreCase) ||
     string.Equals(action, "edit", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(action, AgentProposalOperation.MoveStatus, StringComparison.OrdinalIgnoreCase));

  private static List<string> CollectDependsOnIds(
    ProposalBuildContext ctx,
    JsonElement args,
    JsonElement valuesSource,
    IReadOnlyList<PendingNewReference> orderedNewRefs)
  {
    var dependsOnIds = orderedNewRefs
      .Select(nr => nr.ProposalId)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if ((TryGetPropertyIgnoreCase(args, "dependsOn", out var depProp) ||
         !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, "dependsOn", out depProp)) &&
        depProp.ValueKind == JsonValueKind.Array)
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
         !valuesSource.Equals(args) && TryGetPropertyIgnoreCase(valuesSource, "attachmentIds", out attProp)) &&
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
    ProposalBuildContext ctx) =>
    !string.IsNullOrWhiteSpace(requestedService)
      ? AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, ctx.Services, ctx.Db, ctx.App)
        ?? throw new InvalidOperationException($"Form service '{requestedService}' is not available or not enabled for AI mutations.")
      : throw new InvalidOperationException("Proposal must specify a valid 'service' from available forms.");
}
