using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentProposalExecutor
{
  public static async Task<AgentCommitResultDto> CommitProposalAsync(
    AgentProposalDto proposal,
    IServiceProvider services,
    CancellationToken ct)
  {
    var batchResult = await CommitBatchAsync([proposal.Id], services, ct);
    var primary = batchResult.Committed.FirstOrDefault(c => string.Equals(c.ProposalId, proposal.Id, StringComparison.OrdinalIgnoreCase))
                  ?? batchResult.Committed.LastOrDefault()
                  ?? throw AgentRefusal.Create(Localizer.Instance["AgentErrorSave"]);

    var others = batchResult.Committed
      .Where(c => !string.Equals(c.ProposalId, primary.ProposalId, StringComparison.OrdinalIgnoreCase))
      .ToList();

    return new AgentCommitResultDto
    {
      ProposalId = primary.ProposalId,
      RecordId = primary.RecordId,
      RecordLabel = primary.RecordLabel,
      FormService = primary.FormService,
      RefreshServices = batchResult.RefreshServices,
      AlsoCommitted = others.Count > 0 ? others : null,
      RunId = batchResult.RunId,
      BatchCode = batchResult.BatchCode,
      Warnings = primary.Warnings
    };
  }

  public static async Task<AgentBatchCommitResultDto> CommitBatchAsync(
    IReadOnlyList<string> proposalIds,
    IServiceProvider services,
    CancellationToken ct)
  {
    var store = services.GetRequiredService<AgentStore>();
    var L = Localizer.Instance;
    var owner = AgentStore.ResolveOwner(services);

    var requestedIds = proposalIds
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .Select(id => id.Trim())
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    if (requestedIds.Count == 0)
    {
      return new AgentBatchCommitResultDto();
    }

    var (allProposalsById, payloadsById) = AgentBatchGraphCollector.CollectProposalGraph(requestedIds, store, owner, L);
    var alreadyCommittedDtos = new List<AgentCommitResultDto>();
    var committedIdsByProposalId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    var pendingItems = new List<(AgentProposalDto Proposal, StagedProposalPayload Payload)>();

    foreach (var prop in allProposalsById.Values)
    {
      if (prop.Status == "committed" && prop.CommittedRecordId.HasValue)
      {
        committedIdsByProposalId[prop.Id] = prop.CommittedRecordId.Value;
        alreadyCommittedDtos.Add(ToAlreadyCommittedDto(prop));
      }
      else if (payloadsById.TryGetValue(prop.Id, out var payload))
      {
        pendingItems.Add((prop, payload));
      }
    }

    if (pendingItems.Count == 0)
    {
      return new AgentBatchCommitResultDto { Committed = alreadyCommittedDtos, RefreshServices = [] };
    }

    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var lockedProposals = new List<AgentProposalDto>();

    try
    {
      foreach (var prop in pendingItems.Select(x => x.Proposal).OrderBy(p => p.Id, StringComparer.Ordinal))
      {
        lock (prop)
        {
          if (prop.Status == "committed" && prop.CommittedRecordId.HasValue)
          {
            committedIdsByProposalId[prop.Id] = prop.CommittedRecordId.Value;
            alreadyCommittedDtos.Add(ToAlreadyCommittedDto(prop));
            continue;
          }

          if (prop.Status == "superseded")
          {
            throw AgentRefusal.Create(L["AgentProposalSuperseded"]);
          }

          if (prop.Status == "committing")
          {
            throw AgentRefusal.Create(L["MutationRefused"]);
          }

          prop.Status = "committing";
          lockedProposals.Add(prop);
        }
      }

      var lockedSet = lockedProposals.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var activePending = pendingItems.Where(x => lockedSet.Contains(x.Proposal.Id)).Select(x => x.Proposal).ToList();
      if (activePending.Count == 0)
      {
        return new AgentBatchCommitResultDto { Committed = alreadyCommittedDtos, RefreshServices = [] };
      }

      var sortedProposals = AgentTopologicalSorter.SortStagedProposalsTopologically(activePending, payloadsById);
      var requiredContextTypes = AgentBatchGraphCollector.CollectRequiredContextTypes(sortedProposals, payloadsById);
      var batchCode = Guid.NewGuid().ToString("N")[..6];
      var agentCorrelationId = sortedProposals.Count > 1
        ? $"agent-batch:{batchCode}:{sortedProposals.Count}"
        : $"agent:{batchCode}";
      string? committedRunId = null;

      var stepOutcomes = new List<(AgentProposalDto Proposal, long RecordId, string RecordLabel, string FormServicePath, List<string> RefreshServices, List<MessageDto>? Warnings)>();
      var allRefreshServices = new List<string>();

      using var correlationScope = AsyncLocalTenantContext.UseCorrelation(agentCorrelationId);

      await AgentBatchTransactionRunner.RunAtomicAsync(
        services,
        requiredContextTypes,
        defaultScope.Db,
        defaultScope.App,
        async (scopes, mutationCt) =>
        {
          committedRunId = scopes[0].Db.CurrentMutationRunId?.ToString();
          var preVerifiedEdits = await AgentBatchGraphCollector.PreVerifyInitialVersionsAsync(sortedProposals, payloadsById, scopes, mutationCt);

          foreach (var stepProposal in sortedProposals)
          {
            var stepPayload = payloadsById[stepProposal.Id];
            var outcome = await ExecuteStepCoreAsync(
              stepProposal, stepPayload, committedIdsByProposalId, preVerifiedEdits, scopes, store, owner, L, mutationCt);

            committedIdsByProposalId[stepProposal.Id] = outcome.RecordId;
            stepOutcomes.Add((stepProposal, outcome.RecordId, outcome.RecordLabel, outcome.FormServicePath, outcome.RefreshServices, outcome.Warnings));
          }

          allRefreshServices = stepOutcomes
            .SelectMany(o => o.RefreshServices)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        },
        ct);

      var committedResults = new List<AgentCommitResultDto>(alreadyCommittedDtos);
      foreach (var outcome in stepOutcomes)
      {
        lock (outcome.Proposal)
        {
          outcome.Proposal.Status = "committed";
          outcome.Proposal.CommittedRecordId = outcome.RecordId;
          outcome.Proposal.CommittedLabel = outcome.RecordLabel;
          outcome.Proposal.FormService = outcome.FormServicePath;
        }

        committedResults.Add(new AgentCommitResultDto
        {
          ProposalId = outcome.Proposal.Id,
          RecordId = outcome.RecordId,
          RecordLabel = outcome.RecordLabel,
          FormService = outcome.FormServicePath,
          RefreshServices = allRefreshServices,
          RunId = committedRunId,
          BatchCode = batchCode,
          Warnings = outcome.Warnings
        });
      }

      return new AgentBatchCommitResultDto
      {
        Committed = committedResults,
        RefreshServices = allRefreshServices,
        RunId = committedRunId,
        BatchCode = batchCode
      };
    }
    catch
    {
      foreach (var prop in lockedProposals)
      {
        lock (prop)
        {
          prop.Status = "pending";
          prop.CommittedRecordId = null;
        }
      }

      throw;
    }
  }

  private static async Task<(long RecordId, string RecordLabel, string FormServicePath, List<string> RefreshServices, List<MessageDto>? Warnings)> ExecuteStepCoreAsync(
    AgentProposalDto proposal,
    StagedProposalPayload payload,
    Dictionary<string, long> committedIdsByProposalId,
    HashSet<(Type EntityType, long RecordId)> preVerifiedEdits,
    IReadOnlyList<AgentModuleScope> scopes,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L,
    CancellationToken mutationCt)
  {
    var pendingTopLevelFields = await AgentBatchGraphCollector.ApplyResolvedNewReferencesAsync(
      payload, committedIdsByProposalId, scopes, store, owner, L, mutationCt);

    if (string.Equals(payload.Operation, AgentProposalOperation.MoveStatus, StringComparison.OrdinalIgnoreCase))
    {
      return await ExecuteMoveStatusStepAsync(proposal, payload, preVerifiedEdits, scopes, L, mutationCt);
    }

    var isEdit = payload.RecordId is > 0;
    var formType = ModuleCatalog.Instance.FindServiceType(payload.Service)
      ?? throw AgentRefusal.Create(L["MutationRefused"]);
    var stepScope = AgentBatchTransactionRunner.SelectScopeForService(scopes, formType, scopes[0]);

    if (ModuleCatalog.Instance.CreateServiceInstance(formType, stepScope.Db, stepScope.App) is not IFormService formInst ||
        !formInst.Feature.AllowsAgentWrite ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, stepScope.App))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    formInst.SetRequest(new FormRequest { Id = isEdit ? payload.RecordId : null });
    var ver = await RefreshIntraBatchVersionAsync(formInst, payload, isEdit, preVerifiedEdits, pendingTopLevelFields, mutationCt);
    var (formElements, children) = await formInst.GetElementsMetadataAsync(mutationCt);

    var valueJson = JsonSerializer.SerializeToElement(payload.Values, WebJson);
    object saveResult;
    try
    {
      saveResult = await formInst.SaveAsync(
        valueJson,
        new FormRequest { Id = payload.RecordId, Ver = ver, Value = valueJson },
        mutationCt);
    }
    catch (FormRefusedException refusedEx)
    {
      throw AgentRefusal.Create(FormatRefusedException(refusedEx, formElements), refusedEx);
    }

    if (!TryExtractSavedId(saveResult, out var recordId))
    {
      var refusedDto = saveResult as FormDataDto;
      throw AgentRefusal.Create(
        refusedDto?.Messages?.FirstOrDefault()?.Detail
        ?? refusedDto?.Warnings?.FirstOrDefault()?.Detail
        ?? L["AgentErrorSave"]);
    }

    var collectedWarnings = new List<MessageDto>();
    if (formInst.GetLastSaveWarnings() is { Count: > 0 } parentWarnings)
    {
      collectedWarnings.AddRange(parentWarnings);
    }

    await AgentProposalChildExecutor.ExecuteChildGroupsAsync(
      formInst, recordId, isEdit, payload.Values, children, payload.Children, scopes, stepScope, collectedWarnings, L, mutationCt);

    await AgentBatchGraphCollector.AttachStagedFilesAsync(proposal, children, recordId, formInst.Service, stepScope, store, owner, L, mutationCt);

    var recordLabel = AgentBatchGraphCollector.BuildCommittedRecordLabel(proposal, payload, formInst, formElements, saveResult, recordId, L);
    var refreshServices = CollectModuleGridServices(formInst.Feature.Module);
    return (recordId, recordLabel, formInst.Service, refreshServices, collectedWarnings.Count > 0 ? collectedWarnings : null);
  }

  private static async Task<(long RecordId, string RecordLabel, string FormServicePath, List<string> RefreshServices, List<MessageDto>? Warnings)> ExecuteMoveStatusStepAsync(
    AgentProposalDto proposal,
    StagedProposalPayload payload,
    HashSet<(Type EntityType, long RecordId)> preVerifiedEdits,
    IReadOnlyList<AgentModuleScope> scopes,
    Localizer L,
    CancellationToken mutationCt)
  {
    var (feature, formType, gridType) = AgentProposalFlowBuilder.ResolveMoveTargetFeature(payload.Service);
    var flowType = AgentFlowHelper.FindFlowServiceType(feature);
    if (feature is not { AgentAccess: AgentAccess.ReadWrite } ||
        flowType == null ||
        formType == null && gridType == null ||
        payload.RecordId is not > 0 ||
        string.IsNullOrWhiteSpace(payload.Transition))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var stepScope = AgentBatchTransactionRunner.SelectScopeForService(scopes, formType ?? gridType ?? flowType, scopes[0]);
    var explicitRight = AgentFlowHelper.FindDeclaredTransitionRight(flowType, payload.Transition, stepScope.Db, stepScope.App);
    if (!AgentFlowHelper.CanMoveTransition(feature, stepScope.App.User, payload.Transition, explicitRight))
    {
      throw AgentRefusal.Create(L["MutationRefused"]);
    }

    var (existing, _) = await AgentFlowHelper.EnsureRecordReachableAsync(
      feature,
      formType,
      gridType,
      payload.RecordId.Value,
      stepScope.Db,
      stepScope.App,
      mutationCt);

    var ver = payload.Ver;
    if (existing is IHasVersion versioned &&
        ver != null &&
        versioned.Ver != ver.Value &&
        preVerifiedEdits.Contains((existing.GetType(), payload.RecordId.Value)))
    {
      ver = versioned.Ver;
    }

    JsonElement? askJson = payload.Values.Count > 0 ? JsonSerializer.SerializeToElement(payload.Values, WebJson) : null;
    await AgentFlowHelper.ExecuteMoveStatusAsync(
      formType, gridType, payload.RecordId.Value, payload.Transition, askJson, ver, stepScope.Db, stepScope.App, mutationCt);

    var formPath = formType != null ? ServicePathResolver.Resolve(formType) : payload.Service;
    var label = !string.IsNullOrWhiteSpace(proposal.Title) ? proposal.Title : $"#{payload.RecordId.Value}";
    return (payload.RecordId.Value, label, formPath, CollectModuleGridServices(feature.Module), null);
  }

  private static async Task<int?> RefreshIntraBatchVersionAsync(
    IFormService formInst,
    StagedProposalPayload payload,
    bool isEdit,
    HashSet<(Type EntityType, long RecordId)> preVerifiedEdits,
    HashSet<string> pendingTopLevelFields,
    CancellationToken mutationCt)
  {
    var ver = payload.Ver;
    if (!isEdit)
    {
      return ver;
    }

    var existingEntity = await formInst.LoadParentRecordAsync(payload.RecordId!.Value, mutationCt);
    if (existingEntity is IHasVersion currentVersioned)
    {
      if (ver == null)
      {
        ver = currentVersioned.Ver;
      }
      else if (currentVersioned.Ver != ver.Value &&
               preVerifiedEdits.Contains((existingEntity.GetType(), payload.RecordId.Value)))
      {
        ver = currentVersioned.Ver;
        var explicitSet = payload.ExplicitFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var freshDraft = await formInst.PrepareDraftAsync(payload.RecordId.Value, ct: mutationCt);
        foreach (var (fk, fv) in freshDraft.Values)
        {
          if (!explicitSet.Contains(fk) && !pendingTopLevelFields.Contains(fk))
          {
            payload.Values[fk] = fv;
          }
        }
      }
    }

    return ver;
  }

  private static List<string> CollectModuleGridServices(string moduleHandle) =>
    ModuleCatalog.Instance.AllFeatures
      .Where(f => string.Equals(f.Module, moduleHandle, StringComparison.OrdinalIgnoreCase) &&
                  f.ServiceType != null &&
                  typeof(IGridService).IsAssignableFrom(f.ServiceType))
      .Select(f => ServicePathResolver.Resolve(f.ServiceType!))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

  private static AgentCommitResultDto ToAlreadyCommittedDto(AgentProposalDto prop) =>
    new()
    {
      ProposalId = prop.Id,
      RecordId = prop.CommittedRecordId!.Value,
      RecordLabel = prop.CommittedLabel ?? $"#{prop.CommittedRecordId.Value}",
      FormService = prop.FormService ?? prop.Service,
      RefreshServices = []
    };
}
