using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalFlowBuilder
{
  public static async Task<AgentProposalDto> StageMoveStatusProposalAsync(
    JsonElement args,
    JsonElement valuesSource,
    string requestedTransition,
    long recordId,
    string? requestedService,
    string sessionId,
    string? stepKey,
    string? turnId,
    AgentOwnerScope owner,
    AgentStore store,
    AgentOptions options,
    Func<string, string?, AgentProposalDto?> findReferencedProposal,
    IServiceProvider rootServices,
    PlatformDbContext fallbackDb,
    PlatformAppService fallbackApp,
    Localizer L,
    CancellationToken ct)
  {
    var (feature, formType, gridType) = ResolveMoveTargetFeature(requestedService);
    if (feature == null || !feature.AllowsAgentWrite || formType == null && gridType == null)
    {
      throw new InvalidOperationException($"Status flow on '{requestedService}' is not available or not enabled for AI mutations.");
    }

    var (_, transitions) = await AgentFlowHelper.DiscoverFeatureFlowAsync(feature, rootServices, fallbackDb, fallbackApp, ct);
    if (transitions.Count == 0)
    {
      throw new InvalidOperationException($"No permitted status transitions are available on '{requestedService}'.");
    }

    var matched = transitions.FirstOrDefault(t => string.Equals(t.Name, requestedTransition, StringComparison.OrdinalIgnoreCase))
      ?? transitions.FirstOrDefault(t => string.Equals(t.Label, requestedTransition, StringComparison.OrdinalIgnoreCase))
      ?? throw new InvalidOperationException($"Transition '{requestedTransition}' is not available or permitted.");

    await using var scope = AgentModuleScope.OpenForService(rootServices, formType ?? gridType ?? matched.FlowServiceType, fallbackDb, fallbackApp);

    var (reachableEntity, rowObj) = await AgentFlowHelper.EnsureRecordReachableAsync(
      feature,
      formType,
      gridType,
      recordId,
      scope.Db,
      scope.App,
      ct);

    var (_, currentStatusName, recordVer) = await AgentFlowHelper.ValidateRecordForTransitionAsync(
      matched.FlowServiceType,
      feature,
      recordId,
      reachableEntity,
      matched,
      scope.Db,
      scope.App,
      ct);

    var recordCaption = rowObj != null ? GetRowDisplayName(CleanGridRow(rowObj)) : null;

    var askSource = TryGetPropertyIgnoreCase(args, "ask", out var askEl) && askEl.ValueKind == JsonValueKind.Object
      ? askEl
      : valuesSource;

    var askValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var newReferences = new List<PendingNewReference>();
    var explicitAskFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var askEditable = matched.AskElements.Where(AgentFeatureRegistry.IsEditableElement).ToList();

    var refServices = askEditable
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var (dictionaries, _) = await AgentContextQueryService.LoadSmartDictionariesAsync(
      refServices,
      options.MaxFullDictionarySize,
      [],
      scope.Db,
      scope.App,
      ct);

    foreach (var el in askEditable)
    {
      var hasProp = TryGetPropertyIgnoreCase(askSource, el.Path, out var rawProp) ||
                    !askSource.Equals(args) && TryGetPropertyIgnoreCase(args, el.Path, out rawProp);
      if (!hasProp || rawProp.ValueKind == JsonValueKind.Undefined)
      {
        continue;
      }

      explicitAskFields.Add(el.Path);
      if (el.Model is FormSelectModel select && !string.IsNullOrEmpty(select.ReferenceService))
      {
        if (AgentStepGraphHelper.TryExtractRefKey(rawProp, out var refKey))
        {
          var refTarget = findReferencedProposal(refKey, select.ReferenceService)
            ?? throw new InvalidOperationException(L["MutationRefused"]);
          askValues[el.Path] = refTarget.RecordId is > 0 ? refTarget.RecordId.Value : null;
          newReferences.Add(new PendingNewReference
          {
            FieldPath = el.Path,
            Name = AgentStepGraphHelper.GetProposalShortLabel(refTarget),
            FormService = refTarget.Service,
            ProposalId = refTarget.Id,
            StepKey = refKey,
            StepNumber = refTarget.StepNumber,
            IsExistingRecordEdit = refTarget.RecordId is > 0
          });
          continue;
        }

        var (resolvedId, _, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
          rawProp,
          select.ReferenceService,
          select.ReferenceFilter,
          allowFuzzy: true,
          allowWrongKindFallback: false,
          dictionaries,
          scope.Db,
          scope.App,
          ct,
          select.ReferenceLabelPaths);
        askValues[el.Path] = resolvedId;
      }
      else
      {
        askValues[el.Path] = ConvertJsonValue(rawProp, el.Type);
      }
    }

    if (matched.Ask != null && newReferences.All(r => r.IsExistingRecordEdit))
    {
      var askJson = JsonSerializer.SerializeToElement(askValues, WebJson);
      var parsedAsk = matched.Ask.Read(askJson);
      var moduleLocalizer = Localizer.Instance.For(feature.Module);
      var askFailures = await matched.Ask.ValidateAsync(parsedAsk, scope.Db, scope.App, moduleLocalizer, ct);
      if (askFailures.Count > 0)
      {
        var firstErr = askFailures[0];
        throw new InvalidOperationException(firstErr.Detail ?? firstErr.Title ?? L["MutationRefused"]);
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
        Value = $"{currentStatusName} → {matched.ToStatusName}",
        Badge = L["AgentChangedBadge"],
        Color = matched.ToStatusColor ?? UiColor.Blue
      }
    };

    var askDisplayFields = AgentProposalDisplayBuilder.BuildDisplayFields(
      askEditable,
      [],
      askValues,
      newReferences,
      explicitAskFields,
      dictionaries,
      isEdit: false,
      L);
    fields.AddRange(askDisplayFields);

    var formServicePath = formType != null ? ServicePathResolver.Resolve(formType) : null;
    var primaryServicePath = formServicePath
      ?? (gridType != null ? ServicePathResolver.Resolve(gridType) : ServicePathResolver.Resolve(matched.FlowServiceType));

    OverlayDto? overlay = null;
    if (formType != null &&
        formServicePath != null &&
        ModuleCatalog.Instance.CreateServiceInstance(formType, scope.Db, scope.App) is IFormService formInst)
    {
      overlay = await formInst.OverlayAsync(formServicePath, ct);
    }

    var title = !string.IsNullOrWhiteSpace(recordCaption)
      ? $"#{recordId} · {recordCaption}"
      : $"#{recordId} · {matched.Label}";

    var payload = JsonSerializer.SerializeToElement(
      new StagedProposalPayload
      {
        Operation = AgentProposalOperation.MoveStatus,
        Service = primaryServicePath,
        Transition = matched.Name,
        RecordId = recordId,
        Ver = recordVer,
        StepKey = stepKey,
        Values = askValues,
        ExplicitFields = [.. explicitAskFields],
        DependsOnProposalIds = dependsOnIds,
        NewReferences = newReferences
      },
      WebJson);

    var proposal = new AgentProposalDto
    {
      Id = Guid.NewGuid().ToString("N")[..12],
      SessionId = sessionId,
      Operation = AgentProposalOperation.MoveStatus,
      Service = primaryServicePath,
      Transition = matched.Name,
      RecordId = recordId,
      StepKey = stepKey,
      DependsOn = dependsOnIds.Count > 0 ? dependsOnIds : null,
      Title = title,
      Subtitle = !string.IsNullOrWhiteSpace(recordCaption) ? matched.Label : null,
      Badge = matched.Label,
      BadgeColor = matched.ToStatusColor ?? UiColor.Blue,
      Fields = fields,
      Payload = payload,
      FormService = formServicePath,
      Overlay = overlay,
      TurnId = turnId
    };

    store.StageProposal(proposal, owner);
    return proposal;
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
}
