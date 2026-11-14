using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalFlowBuilder
{
  public static async Task<AgentProposalDto> StageMoveStatusProposalAsync(
    ProposalBuildContext ctx,
    JsonElement args,
    string requestedTransition,
    long? recordId,
    string? targetRefKey,
    string? requestedService,
    CancellationToken ct)
  {
    var valuesSource = TryGetPropertyIgnoreCase(args, "values", out var vProp) && vProp.ValueKind == JsonValueKind.Object ? vProp : args;
    var stepKey = AgentStepGraphHelper.TryExtractStepKey(args);

    var (feature, formType, gridType) = ResolveMoveTargetFeature(requestedService);
    if (feature is not { AllowsAgentWrite: true } || formType == null && gridType == null)
    {
      throw new InvalidOperationException($"Status flow on '{requestedService}' is not available or not enabled for AI mutations.");
    }

    await using var scope = AgentModuleScope.OpenForService(ctx.Services, formType ?? gridType, ctx.Db, ctx.App);
    if (!AgentFeatureRegistry.CanViewFeature(feature, scope.App))
    {
      throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
    }

    var formInst = formType != null ? ModuleCatalog.Instance.CreateServiceInstance(formType, scope.Db, scope.App) as IFormService : null;
    var gridInst = gridType != null ? ModuleCatalog.Instance.CreateServiceInstance(gridType, scope.Db, scope.App) as IGridService : null;

    var flowDesc = (formInst != null ? await formInst.DescribeStatusFlowAsync(ct) : null)
      ?? (gridInst != null ? await gridInst.DescribeStatusFlowAsync(ct) : null);

    if (flowDesc == null || flowDesc.Transitions.Count == 0)
    {
      throw new InvalidOperationException($"No permitted status transitions are available on '{requestedService}'.");
    }

    var matched = flowDesc.Transitions.FirstOrDefault(t => string.Equals(t.Name, requestedTransition, StringComparison.OrdinalIgnoreCase))
      ?? flowDesc.Transitions.FirstOrDefault(t => string.Equals(t.Label, requestedTransition, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidOperationException($"Transition '{requestedTransition}' is not available or permitted.");

    var formServicePath = formType != null ? ServicePathResolver.Resolve(formType) : null;
    var primaryServicePath = formServicePath ?? ServicePathResolver.Resolve(gridType!);

    var newReferences = new List<PendingNewReference>();
    var effectiveId = recordId;
    string? pendingTargetName = null;

    if (!string.IsNullOrWhiteSpace(targetRefKey))
    {
      var refTarget = ctx.FindReferencedProposal(targetRefKey, primaryServicePath)
        ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
      effectiveId = refTarget.RecordId is > 0 ? refTarget.RecordId.Value : null;
      pendingTargetName = AgentStepGraphHelper.GetProposalShortLabel(refTarget);
      newReferences.Add(new PendingNewReference
      {
        FieldPath = "__targetId",
        Name = pendingTargetName,
        FormService = refTarget.Service,
        ProposalId = refTarget.Id,
        StepKey = targetRefKey,
        StepNumber = refTarget.StepNumber,
        IsExistingRecordEdit = refTarget.RecordId is > 0
      });
    }

    if (effectiveId is > 0 && gridInst != null && await gridInst.ReadRowAsync(effectiveId.Value, ct) == null)
    {
      throw new KeyNotFoundException(ctx.Localizer["FormNotFound"]);
    }

    var askEditable = matched.AskElements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
    var (askValues, explicitAskFields, skipValidationPaths, dictionaries) = await AgentReferenceResolver.ParseAskInputsAsync(
      ctx, args, valuesSource, askEditable, newReferences, scope.Db, scope.App, ct);

    var askJson = JsonSerializer.SerializeToElement(askValues, WebJson);
    StatusMovePreviewDto preview;
    try
    {
      preview = formInst != null && await formInst.DescribeStatusFlowAsync(ct) != null
        ? await formInst.PreviewMoveStatusAsync(effectiveId, matched.Name, askJson, skipValidationPaths, ct)
        : await gridInst!.PreviewMoveStatusAsync(effectiveId, matched.Name, askJson, skipValidationPaths, ct);
    }
    catch (FormRefusedException refusedEx)
    {
      throw new InvalidOperationException(FormatRefusedException(refusedEx, matched.AskElements), refusedEx);
    }

    foreach (var (k, v) in preview.AskValues)
    {
      if (!explicitAskFields.Contains(k) && !skipValidationPaths.Contains(k) && v != null)
      {
        askValues[k] = v;
      }
    }

    var dependsOnIds = newReferences
      .Select(nr => nr.ProposalId)
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id!)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var statusLabel = Localizer.Instance.For(feature.Module)["Status"];
    var fields = new List<AgentProposalFieldDto>
    {
      new()
      {
        Label = statusLabel,
        Value = $"{preview.CurrentStatusName} → {preview.TargetStatusName}",
        Badge = ctx.Localizer["AgentChangedBadge"],
        Color = preview.TargetStatusColor ?? UiColor.Blue
      }
    };

    fields.AddRange(AgentProposalDisplayBuilder.BuildDisplayFields(
      askEditable, [], askValues, newReferences, explicitAskFields, dictionaries, isEdit: false, ctx.Localizer));

    var overlay = formInst != null && formServicePath != null
      ? await formInst.OverlayAsync(formServicePath, ct)
      : null;

    var caption = preview.RecordCaption ?? pendingTargetName;
    var title = BuildMoveTitle(effectiveId, caption, preview.TransitionLabel);

    var payload = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Operation = AgentProposalOperation.MoveStatus,
        Service = primaryServicePath,
        Transition = preview.TransitionName,
        RecordId = effectiveId,
        Ver = preview.Ver,
        StepKey = stepKey,
        Values = askValues,
        ExplicitFields = [.. explicitAskFields],
        DependsOnProposalIds = dependsOnIds,
        NewReferences = newReferences
      },
      WebJson);

    var warningTexts = preview.Warnings
      .Select(w => w.Detail ?? w.Title)
      .Where(d => !string.IsNullOrWhiteSpace(d))
      .Select(d => d!)
      .Distinct(StringComparer.Ordinal)
      .ToList();

    var proposal = new AgentProposalDto
    {
      Id = Guid.NewGuid().ToString("N")[..12],
      SessionId = ctx.SessionId,
      Operation = AgentProposalOperation.MoveStatus,
      Service = primaryServicePath,
      Transition = preview.TransitionName,
      RecordId = effectiveId,
      StepKey = stepKey,
      DependsOn = dependsOnIds.Count > 0 ? dependsOnIds : null,
      Title = title,
      Subtitle = effectiveId is > 0 && !string.IsNullOrWhiteSpace(caption) ? preview.TransitionLabel : null,
      Badge = preview.TransitionLabel,
      BadgeColor = preview.TargetStatusColor ?? UiColor.Blue,
      Fields = fields,
      Warnings = warningTexts.Count > 0 ? warningTexts : null,
      Payload = payload,
      FormService = formServicePath,
      Overlay = overlay,
      TurnId = ctx.TurnId
    };

    ctx.Store.StageProposal(proposal, ctx.Owner);
    return proposal;
  }

  public static async Task<AgentStepCommitOutcome> ExecuteMoveStatusStepAsync(
    AgentProposalDto proposal,
    StagedProposalPayload payload,
    HashSet<(Type EntityType, long RecordId)> preVerifiedEdits,
    IReadOnlyList<AgentModuleScope> scopes,
    IReadOnlyList<string> refreshServices,
    Localizer L,
    CancellationToken mutationCt)
  {
    var (feature, formType, gridType) = ResolveMoveTargetFeature(payload.Service);
    if (feature is not { AllowsAgentWrite: true } ||
        formType == null && gridType == null ||
        payload.RecordId is not > 0 ||
        string.IsNullOrWhiteSpace(payload.Transition))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var stepScope = AgentModuleScope.SelectScopeForService(scopes, formType ?? gridType, scopes[0]);
    if (!AgentFeatureRegistry.CanViewFeature(feature, stepScope.App))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var formInst = formType != null ? ModuleCatalog.Instance.CreateServiceInstance(formType, stepScope.Db, stepScope.App) as IFormService : null;
    var gridInst = gridType != null ? ModuleCatalog.Instance.CreateServiceInstance(gridType, stepScope.Db, stepScope.App) as IGridService : null;

    if (gridInst != null && await gridInst.ReadRowAsync(payload.RecordId.Value, mutationCt) == null)
    {
      throw new KeyNotFoundException(L["FormNotFound"]);
    }

    JsonElement? askJson = payload.Values.Count > 0 ? JsonSerializer.SerializeToElement(payload.Values, WebJson) : null;
    StatusMovePreviewDto livePreview;
    try
    {
      livePreview = formInst != null && await formInst.DescribeStatusFlowAsync(mutationCt) != null
        ? await formInst.PreviewMoveStatusAsync(payload.RecordId.Value, payload.Transition, askJson, ct: mutationCt)
        : await gridInst!.PreviewMoveStatusAsync(payload.RecordId.Value, payload.Transition, askJson, ct: mutationCt);
    }
    catch (FormRefusedException refusedEx)
    {
      throw AgentRefusal.Create(refusedEx.Messages.FirstOrDefault()?.Detail ?? refusedEx.Message, refusedEx);
    }

    var ver = payload.Ver;
    var entityType = ModuleCatalog.GetServiceEntityType(formType ?? gridType);
    if (livePreview.Ver != null &&
        entityType != null &&
        preVerifiedEdits.Contains((entityType, payload.RecordId.Value)) &&
        (ver == null || ver.Value != livePreview.Ver.Value))
    {
      ver = livePreview.Ver;
    }

    List<MessageDto>? warnings;
    if (formInst != null && await formInst.DescribeStatusFlowAsync(mutationCt) != null)
    {
      var res = await formInst.MoveStatusAsync(payload.RecordId.Value, payload.Transition, askJson, ver, mutationCt);
      if (res.Outcome != MutationOutcome.Committed)
      {
        var fallback = res.Outcome == MutationOutcome.Conflict ? L["FormChangedElsewhere"] : L["MutationRefused"];
        throw AgentRefusal.Create(res.Messages?.FirstOrDefault()?.Detail ?? fallback);
      }

      warnings = res.Warnings;
    }
    else
    {
      var gridReq = new GridRequest
      {
        SelectedIds = [payload.RecordId.Value],
        SelectedVersions = ver.HasValue ? [ver.Value] : null
      };
      var res = await gridInst!.MoveStatusAsync(gridReq, payload.Transition, askJson, mutationCt);
      if (res.Outcome != MutationOutcome.Committed)
      {
        var fallback = res.Outcome == MutationOutcome.Conflict ? L["FormChangedElsewhere"] : L["MutationRefused"];
        throw AgentRefusal.Create(res.Messages?.FirstOrDefault()?.Detail ?? fallback);
      }

      warnings = res.Warnings;
    }

    var formPath = formType != null ? ServicePathResolver.Resolve(formType) : payload.Service;
    var label = !string.IsNullOrWhiteSpace(proposal.Title) ? proposal.Title : $"#{payload.RecordId.Value}";
    return new AgentStepCommitOutcome(payload.RecordId.Value, label, formPath, [.. refreshServices], warnings);
  }

  internal static (Feature? Feature, Type? FormServiceType, Type? GridServiceType) ResolveMoveTargetFeature(string? requestedService)
  {
    if (string.IsNullOrWhiteSpace(requestedService))
    {
      return (null, null, null);
    }

    var directType = ModuleCatalog.Instance.FindServiceType(requestedService.Trim());
    if (directType == null)
    {
      var norm = AgentFeatureRegistry.NormalizeServiceKey(requestedService);
      directType = ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
        !t.IsAbstract &&
        string.Equals(AgentFeatureRegistry.NormalizeServiceKey(ServicePathResolver.Resolve(t)), norm, StringComparison.OrdinalIgnoreCase));
    }

    var feature = directType != null ? ModuleCatalog.Instance.FindFeature(directType) : null;
    if (feature == null)
    {
      return (null, null, null);
    }

    var formType = directType != null && typeof(IFormService).IsAssignableFrom(directType) ? directType : null;
    var gridType = directType != null && typeof(IGridService).IsAssignableFrom(directType) ? directType : null;

    if (gridType == null && feature.ServiceType != null && typeof(IGridService).IsAssignableFrom(feature.ServiceType))
    {
      gridType = feature.ServiceType;
    }

    if (formType == null)
    {
      formType = ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
        !t.IsAbstract && typeof(IFormService).IsAssignableFrom(t) && ModuleCatalog.Instance.FindFeature(t) == feature);
    }

    return (feature, formType, gridType);
  }

  private static string BuildMoveTitle(long? effectiveId, string? caption, string transitionLabel)
  {
    if (effectiveId is > 0)
    {
      return !string.IsNullOrWhiteSpace(caption)
        ? $"#{effectiveId.Value} · {caption}"
        : $"#{effectiveId.Value} · {transitionLabel}";
    }

    return !string.IsNullOrWhiteSpace(caption) ? $"{transitionLabel}: {caption}" : transitionLabel;
  }
}
