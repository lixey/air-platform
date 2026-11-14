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
        var (_, formType, gridType) = AgentProposalFlowBuilder.ResolveMoveTargetFeature(payload.Service);
        if (AgentModuleScope.ResolveTargetContextType(formType ?? gridType) is { } flowCtx)
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
      var checkType = ModuleCatalog.Instance.FindServiceType(stepPayload.Service);
      if (checkType == null || !typeof(IFormService).IsAssignableFrom(checkType))
      {
        var (_, resolvedForm, _) = AgentProposalFlowBuilder.ResolveMoveTargetFeature(stepPayload.Service);
        checkType = resolvedForm ?? checkType;
      }

      if (checkType == null)
      {
        continue;
      }

      var checkScope = AgentModuleScope.SelectScopeForService(scopes, checkType, scopes[0]);
      if (typeof(IFormService).IsAssignableFrom(checkType) &&
          ModuleCatalog.Instance.CreateServiceInstance(checkType, checkScope.Db, checkScope.App) is IFormService checkForm)
      {
        if (stepPayload.RecordId is > 0 && stepPayload.Ver.HasValue)
        {
          checkForm.SetRequest(new FormRequest { Id = stepPayload.RecordId });
          var existing = await checkForm.LoadParentRecordAsync(stepPayload.RecordId.Value, mutationCt);
          if (existing is IHasVersion versioned && versioned.Ver == stepPayload.Ver.Value)
          {
            preVerified.Add((existing.GetType(), stepPayload.RecordId.Value));
          }
        }
      }

      if (stepPayload.SelectedIds.Count > 0 &&
          stepPayload.SelectedVersions != null &&
          stepPayload.SelectedIds.Count == stepPayload.SelectedVersions.Count &&
          typeof(IGridService).IsAssignableFrom(checkType) &&
          ModuleCatalog.Instance.CreateServiceInstance(checkType, checkScope.Db, checkScope.App) is IGridService checkGrid &&
          ModuleCatalog.GetServiceEntityType(checkType) is { } gridEntityType)
      {
        DeclaredActionPreviewDto? preview = null;
        if (!string.IsNullOrEmpty(stepPayload.Action))
        {
          JsonElement? askJson = stepPayload.Values.Count > 0
            ? JsonSerializer.SerializeToElement(stepPayload.Values, WebJson)
            : null;
          var skipPaths = stepPayload.NewReferences
            .Where(r => !r.FieldPath.StartsWith("__", StringComparison.Ordinal))
            .Select(r => r.FieldPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
          try
          {
            preview = await checkGrid.PreviewDeclaredActionAsync(
              stepPayload.Action,
              stepPayload.SelectedIds.Where(id => id > 0).ToList(),
              askJson,
              skipPaths.Count > 0 ? skipPaths : null,
              ct: mutationCt);
          }
          catch (FormRefusedException)
          {
            preview = null;
          }
        }

        if (preview?.TargetVersions != null && preview.TargetVersions.Count == stepPayload.SelectedVersions.Count)
        {
          for (var i = 0; i < stepPayload.SelectedIds.Count; i++)
          {
            if (stepPayload.SelectedIds[i] > 0 && preview.TargetVersions[i] == stepPayload.SelectedVersions[i])
            {
              preVerified.Add((gridEntityType, stepPayload.SelectedIds[i]));
            }
          }
        }
      }
    }

    return preVerified;
  }

  public static HashSet<string> ApplyResolvedNewReferences(
    StagedProposalPayload payload,
    Dictionary<string, long> committedIdsByProposalId,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L)
  {
    var pendingTopLevelFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var pendingRef in payload.NewReferences)
    {
      if (string.IsNullOrEmpty(pendingRef.ProposalId))
      {
        throw AgentRefusal.Create(L["MutationRefused"]);
      }

      var committedRefId = ResolveCommittedProposalRecordId(pendingRef.ProposalId, committedIdsByProposalId, store, owner, L);
      if (committedRefId <= 0)
      {
        throw AgentRefusal.Create(L["MutationRefused"]);
      }

      if (string.Equals(pendingRef.FieldPath, "__targetId", StringComparison.OrdinalIgnoreCase))
      {
        payload.RecordId = committedRefId;
        if (payload.SelectedIds.Count == 1)
        {
          payload.SelectedIds[0] = committedRefId;
        }

        continue;
      }

      if (pendingRef.FieldPath.StartsWith("__selectedId:", StringComparison.OrdinalIgnoreCase) &&
          int.TryParse(pendingRef.FieldPath["__selectedId:".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var selIdx) &&
          selIdx >= 0 && selIdx < payload.SelectedIds.Count)
      {
        payload.SelectedIds[selIdx] = committedRefId;
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
          targetChild.Rows[lineIdx][pendingRef.FieldPath] = committedRefId;
        }
      }
      else
      {
        pendingTopLevelFields.Add(pendingRef.FieldPath);
        if (pendingRef.IsMultiple)
        {
          var idList = ExtractLongList(payload.Values.GetValueOrDefault(pendingRef.FieldPath));
          if (!idList.Contains(committedRefId))
          {
            idList.Add(committedRefId);
          }

          payload.Values[pendingRef.FieldPath] = idList;
        }
        else
        {
          payload.Values[pendingRef.FieldPath] = committedRefId;
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
    object? savedEntity,
    long recordId,
    Localizer L)
  {
    if (savedEntity != null)
    {
      var caption = formInst.GetRecordCaption(savedEntity, recordId);
      var genericCaption = L.For(formInst.Feature.Module)["FormRecord", formInst.GetTitle(), recordId];
      if (!string.IsNullOrWhiteSpace(caption) && !string.Equals(caption, genericCaption, StringComparison.Ordinal))
      {
        return caption.Contains($"#{recordId}", StringComparison.Ordinal) ? caption : $"{caption} (#{recordId})";
      }
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
