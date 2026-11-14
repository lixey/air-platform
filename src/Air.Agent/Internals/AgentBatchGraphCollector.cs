using Platform.App.Files.Internals;
using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentBatchGraphCollector
{
  public static (Dictionary<string, AgentProposalDto> Proposals, Dictionary<string, StagedProposalPayload> Payloads) CollectProposalGraph(
    List<string> requestedIds,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L)
  {
    var allProposalsById = new Dictionary<string, AgentProposalDto>(StringComparer.OrdinalIgnoreCase);
    var payloadsById = new Dictionary<string, StagedProposalPayload>(StringComparer.OrdinalIgnoreCase);
    var queue = new Queue<string>(requestedIds);

    while (queue.Count > 0)
    {
      var currentId = queue.Dequeue();
      if (allProposalsById.ContainsKey(currentId))
      {
        continue;
      }

      var prop = store.GetVerifiedProposal(currentId, owner) ?? throw AgentRefusal.Create(L["AgentProposalNotFound"]);
      if (prop.Status == "superseded")
      {
        throw AgentRefusal.Create(L["AgentProposalSuperseded"]);
      }

      allProposalsById[prop.Id] = prop;
      if (prop.Status == "committed" && prop.CommittedRecordId.HasValue)
      {
        continue;
      }

      var payload = prop.Payload.Deserialize<StagedProposalPayload>(WebJson)
        ?? throw AgentRefusal.Create(L["AgentErrorSave"]);
      payloadsById[prop.Id] = payload;

      var prereqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (prop.DependsOn is { Count: > 0 })
      {
        prereqIds.UnionWith(prop.DependsOn.Where(id => !string.IsNullOrWhiteSpace(id)));
      }

      prereqIds.UnionWith(payload.DependsOnProposalIds.Where(id => !string.IsNullOrWhiteSpace(id)));

      foreach (var pendingRef in payload.NewReferences)
      {
        if (!string.IsNullOrWhiteSpace(pendingRef.ProposalId))
        {
          prereqIds.Add(pendingRef.ProposalId);
        }
        else if (!string.IsNullOrWhiteSpace(pendingRef.FormService))
        {
          var matchedPrereq = store.GetSessionProposals(prop.SessionId, owner).FirstOrDefault(p =>
            p.Id != prop.Id &&
            p.Status is "pending" or "committed" &&
            (prop.TurnId == null || string.Equals(p.TurnId, prop.TurnId, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(p.Service, pendingRef.FormService, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(AgentStepGraphHelper.GetProposalShortLabel(p), pendingRef.Name, StringComparison.OrdinalIgnoreCase));
          if (matchedPrereq != null)
          {
            pendingRef.ProposalId = matchedPrereq.Id;
            prereqIds.Add(matchedPrereq.Id);
          }
        }
      }

      foreach (var prereqId in prereqIds)
      {
        if (!allProposalsById.ContainsKey(prereqId) && store.GetVerifiedProposal(prereqId, owner) != null)
        {
          queue.Enqueue(prereqId);
        }
      }
    }

    return (allProposalsById, payloadsById);
  }

  public static List<Type> CollectRequiredContextTypes(
    IReadOnlyList<AgentProposalDto> sortedProposals,
    Dictionary<string, StagedProposalPayload> payloadsById)
  {
    var types = new List<Type>();
    void AddService(string? servicePath)
    {
      if (!string.IsNullOrWhiteSpace(servicePath) &&
          ModuleCatalog.Instance.FindServiceType(servicePath) is { } svcType &&
          AgentModuleScope.ResolveTargetContextType(svcType) is { } ctxType)
      {
        types.Add(ctxType);
      }
    }

    foreach (var prop in sortedProposals)
    {
      var payload = payloadsById[prop.Id];
      AddService(payload.Service);
      if (string.Equals(payload.Operation, AgentProposalOperation.MoveStatus, StringComparison.OrdinalIgnoreCase))
      {
        var (feat, formType, gridType) = AgentProposalFlowBuilder.ResolveMoveTargetFeature(payload.Service);
        var flowType = AgentFlowHelper.FindFlowServiceType(feat);
        if (AgentModuleScope.ResolveTargetContextType(formType ?? gridType ?? flowType) is { } flowCtx)
        {
          types.Add(flowCtx);
        }
      }

      foreach (var nr in payload.NewReferences)
      {
        AddService(nr.FormService);
      }

      foreach (var cg in payload.Children)
      {
        AddService(cg.ChildFormService);
        AddService(cg.ChildGridService);
      }
    }

    return types;
  }

  public static async Task<HashSet<(Type EntityType, long RecordId)>> PreVerifyInitialVersionsAsync(
    IReadOnlyList<AgentProposalDto> sortedProposals,
    Dictionary<string, StagedProposalPayload> payloadsById,
    IReadOnlyList<AgentModuleScope> scopes,
    CancellationToken mutationCt)
  {
    var preVerified = new HashSet<(Type EntityType, long RecordId)>();
    foreach (var stepProp in sortedProposals)
    {
      var stepPayload = payloadsById[stepProp.Id];
      if (stepPayload.RecordId is not > 0 || !stepPayload.Ver.HasValue)
      {
        continue;
      }

      var checkFormType = ModuleCatalog.Instance.FindServiceType(stepPayload.Service);
      if (checkFormType == null || !typeof(IFormService).IsAssignableFrom(checkFormType))
      {
        var (_, resolvedForm, _) = AgentProposalFlowBuilder.ResolveMoveTargetFeature(stepPayload.Service);
        checkFormType = resolvedForm;
      }

      if (checkFormType == null)
      {
        continue;
      }

      var checkScope = AgentBatchTransactionRunner.SelectScopeForService(scopes, checkFormType, scopes[0]);
      if (ModuleCatalog.Instance.CreateServiceInstance(checkFormType, checkScope.Db, checkScope.App) is not IFormService checkForm)
      {
        continue;
      }

      checkForm.SetRequest(new FormRequest { Id = stepPayload.RecordId });
      var existing = await checkForm.LoadParentRecordAsync(stepPayload.RecordId.Value, mutationCt);
      if (existing is IHasVersion versioned && versioned.Ver == stepPayload.Ver.Value)
      {
        preVerified.Add((existing.GetType(), stepPayload.RecordId.Value));
      }
    }

    return preVerified;
  }

  public static async Task<HashSet<string>> ApplyResolvedNewReferencesAsync(
    StagedProposalPayload payload,
    Dictionary<string, long> committedIdsByProposalId,
    IReadOnlyList<AgentModuleScope> scopes,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L,
    CancellationToken mutationCt)
  {
    var pendingTopLevelFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var pendingRef in payload.NewReferences)
    {
      long? committedRefId = null;
      if (!string.IsNullOrEmpty(pendingRef.ProposalId))
      {
        committedRefId = ResolveCommittedProposalRecordId(pendingRef.ProposalId, committedIdsByProposalId, store, owner, L);
      }
      else if (!string.IsNullOrEmpty(pendingRef.FormService))
      {
        var refFormType = ModuleCatalog.Instance.FindServiceType(pendingRef.FormService);
        var refScope = AgentBatchTransactionRunner.SelectScopeForService(scopes, refFormType, scopes[0]);
        if (refFormType != null &&
            ModuleCatalog.Instance.CreateServiceInstance(refFormType, refScope.Db, refScope.App) is IFormService refFormInst &&
            refFormInst.Feature.AllowsAgentWrite &&
            AgentFeatureRegistry.CanEditFeature(refFormInst.Feature, refScope.App))
        {
          refFormInst.SetRequest(new FormRequest());
          var (refEls, _) = await refFormInst.GetElementsMetadataAsync(mutationCt);
          var refDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(refFormInst, mutationCt);
          foreach (var (k, v) in pendingRef.ExtraValues)
          {
            refDefaults[k] = v;
          }

          refDefaults[ResolvePrimaryTextFieldPath(refEls)] = pendingRef.Name;
          var refJson = JsonSerializer.SerializeToElement(refDefaults, WebJson);
          try
          {
            var refSaved = await refFormInst.SaveAsync(refJson, new FormRequest { Value = refJson }, mutationCt);
            if (TryExtractSavedId(refSaved, out var refId))
            {
              committedRefId = refId;
            }
          }
          catch (FormRefusedException refusedEx)
          {
            throw AgentRefusal.Create(FormatRefusedException(refusedEx, refEls, pendingRef.Name), refusedEx);
          }
        }
      }

      if (committedRefId is not > 0)
      {
        continue;
      }

      if (pendingRef.LineIndex is >= 0 and var lineIdx)
      {
        var targetChild = !string.IsNullOrWhiteSpace(pendingRef.ChildKey)
          ? payload.Children.FirstOrDefault(cg =>
              string.Equals(cg.ChildKey, pendingRef.ChildKey, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(cg.ChildGridService, pendingRef.ChildKey, StringComparison.OrdinalIgnoreCase))
          : payload.Children.FirstOrDefault();

        if (targetChild != null && lineIdx < targetChild.Rows.Count)
        {
          targetChild.Rows[lineIdx][pendingRef.FieldPath] = committedRefId.Value;
        }
      }
      else
      {
        pendingTopLevelFields.Add(pendingRef.FieldPath);
        if (pendingRef.IsMultiple)
        {
          var idList = ExtractLongList(payload.Values.GetValueOrDefault(pendingRef.FieldPath));
          if (!idList.Contains(committedRefId.Value))
          {
            idList.Add(committedRefId.Value);
          }

          payload.Values[pendingRef.FieldPath] = idList;
        }
        else
        {
          payload.Values[pendingRef.FieldPath] = committedRefId.Value;
        }
      }
    }

    return pendingTopLevelFields;
  }

  public static async Task AttachStagedFilesAsync(
    AgentProposalDto proposal,
    IReadOnlyList<ChildGridBuilder> children,
    long recordId,
    string parentService,
    AgentModuleScope stepScope,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L,
    CancellationToken mutationCt)
  {
    if (proposal.AttachmentIds.Count == 0)
    {
      return;
    }

    var fileChild = children.FirstOrDefault(c => c.IsFiles && c.ServiceType != null);
    if (fileChild?.ServiceType == null ||
        ModuleCatalog.Instance.CreateServiceInstance(fileChild.ServiceType, stepScope.Db, stepScope.App) is not IFormFilesSource fileSource)
    {
      return;
    }

    foreach (var attId in proposal.AttachmentIds)
    {
      var att = store.GetAttachment(attId, owner);
      if (att != null && File.Exists(att.LocalPath))
      {
        var bytes = await File.ReadAllBytesAsync(att.LocalPath, mutationCt);
        var attachRes = await fileSource.AttachFileAsync(recordId, att.FileName, att.ContentType, bytes, parentService, mutationCt);
        if (attachRes.Outcome != MutationOutcome.Committed || attachRes.Id is not > 0)
        {
          var msg = attachRes.Messages?.FirstOrDefault()?.Detail ?? attachRes.Warnings?.FirstOrDefault()?.Detail ?? attachRes.Title ?? L["MutationRefused"];
          throw AgentRefusal.Create(msg);
        }
      }
    }
  }

  public static string BuildCommittedRecordLabel(
    AgentProposalDto proposal,
    StagedProposalPayload payload,
    IFormService formInst,
    IReadOnlyList<FormElementDto> formElements,
    object saveResult,
    long recordId,
    Localizer L)
  {
    var caption = formInst.GetRecordCaption(saveResult, recordId);
    var genericCaption = L.For(formInst.Feature.Module)["FormRecord", formInst.GetTitle(), recordId];
    if (!string.IsNullOrWhiteSpace(caption) && !string.Equals(caption, genericCaption, StringComparison.Ordinal))
    {
      return caption.Contains($"#{recordId}", StringComparison.Ordinal) ? caption : $"{caption} (#{recordId})";
    }

    var primaryTextKey = ResolvePrimaryTextFieldPath(formElements);
    if (payload.Values.TryGetValue(primaryTextKey, out var nameObj))
    {
      var str = nameObj is JsonElement { ValueKind: JsonValueKind.String } je ? je.GetString() : nameObj as string;
      if (!string.IsNullOrWhiteSpace(str))
      {
        return $"{str} (#{recordId})";
      }
    }

    if (!string.IsNullOrWhiteSpace(proposal.Title))
    {
      return proposal.Title.StartsWith($"#{recordId}", StringComparison.Ordinal) ? proposal.Title : $"{proposal.Title} (#{recordId})";
    }

    return $"#{recordId}";
  }

  private static long ResolveCommittedProposalRecordId(
    string proposalId,
    Dictionary<string, long> committedIdsByProposalId,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L)
  {
    if (committedIdsByProposalId.TryGetValue(proposalId, out var batchId) && batchId > 0)
    {
      return batchId;
    }

    if (store.GetVerifiedProposal(proposalId, owner) is { Status: "committed", CommittedRecordId: > 0 } existing)
    {
      return existing.CommittedRecordId.Value;
    }

    throw AgentRefusal.Create(L["AgentErrorSave"]);
  }
}
