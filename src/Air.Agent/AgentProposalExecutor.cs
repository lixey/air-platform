using Platform.Agent.Internals;
using Platform.App.Files.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentProposalExecutor
{
  public static async Task<AgentCommitResultDto> CommitProposalAsync(
    AgentProposalDto proposal,
    IServiceProvider services,
    CancellationToken ct
  )
  {
    var batchResult = await CommitBatchAsync([proposal.Id], services, ct);
    var primary = batchResult.Committed.FirstOrDefault(c => string.Equals(c.ProposalId, proposal.Id, StringComparison.OrdinalIgnoreCase))
                  ?? batchResult.Committed.LastOrDefault()
                  ?? throw new InvalidOperationException(Localizer.Instance["AgentErrorSave"]);

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
      BatchCode = batchResult.BatchCode
    };
  }

  public static async Task<AgentBatchCommitResultDto> CommitBatchAsync(
    IReadOnlyList<string> proposalIds,
    IServiceProvider services,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<PlatformDbContext>();
    var app = services.GetRequiredService<PlatformAppService>();
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

      var prop = store.GetProposal(currentId, owner)
                 ?? throw new InvalidOperationException(L["AgentProposalNotFound"]);

      if (prop.Status == "superseded")
      {
        throw new InvalidOperationException(L["AgentProposalSuperseded"]);
      }

      allProposalsById[prop.Id] = prop;

      if (prop.Status == "committed" && prop.CommittedRecordId.HasValue)
      {
        continue;
      }

      var payload = prop.Payload.Deserialize<StagedProposalPayload>(WebJson)
                    ?? throw new InvalidOperationException(L["AgentErrorSave"]);
      payloadsById[prop.Id] = payload;

      var prereqIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (prop.DependsOn is { Count: > 0 })
      {
        foreach (var depId in prop.DependsOn)
        {
          if (!string.IsNullOrWhiteSpace(depId))
          {
            prereqIds.Add(depId);
          }
        }
      }

      foreach (var depId in payload.DependsOnProposalIds)
      {
        if (!string.IsNullOrWhiteSpace(depId))
        {
          prereqIds.Add(depId);
        }
      }

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
            string.Equals(AgentStepGraphHelper.GetProposalShortLabel(p), pendingRef.Name, StringComparison.OrdinalIgnoreCase)
          );
          if (matchedPrereq != null)
          {
            pendingRef.ProposalId = matchedPrereq.Id;
            prereqIds.Add(matchedPrereq.Id);
          }
        }
      }

      foreach (var prereqId in prereqIds)
      {
        if (!allProposalsById.ContainsKey(prereqId) && store.GetProposal(prereqId, owner) != null)
        {
          queue.Enqueue(prereqId);
        }
      }
    }

    var alreadyCommittedDtos = new List<AgentCommitResultDto>();
    var committedIdsByProposalId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    var pendingItems = new List<(AgentProposalDto Proposal, StagedProposalPayload Payload)>();

    foreach (var prop in allProposalsById.Values)
    {
      if (prop.Status == "committed" && prop.CommittedRecordId.HasValue)
      {
        committedIdsByProposalId[prop.Id] = prop.CommittedRecordId.Value;
        alreadyCommittedDtos.Add(
          new AgentCommitResultDto
          {
            ProposalId = prop.Id,
            RecordId = prop.CommittedRecordId.Value,
            RecordLabel = prop.CommittedLabel ?? $"#{prop.CommittedRecordId.Value}",
            FormService = prop.FormService ?? prop.Service,
            RefreshServices = []
          }
        );
      }
      else if (payloadsById.TryGetValue(prop.Id, out var payload))
      {
        pendingItems.Add((prop, payload));
      }
    }

    if (pendingItems.Count == 0)
    {
      return new AgentBatchCommitResultDto
      {
        Committed = alreadyCommittedDtos,
        RefreshServices = []
      };
    }

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
            alreadyCommittedDtos.Add(
              new AgentCommitResultDto
              {
                ProposalId = prop.Id,
                RecordId = prop.CommittedRecordId.Value,
                RecordLabel = prop.CommittedLabel ?? $"#{prop.CommittedRecordId.Value}",
                FormService = prop.FormService ?? prop.Service,
                RefreshServices = []
              }
            );
            continue;
          }

          if (prop.Status == "superseded")
          {
            throw new InvalidOperationException(L["AgentProposalSuperseded"]);
          }

          if (prop.Status == "committing")
          {
            throw new InvalidOperationException(L["MutationRefused"]);
          }

          prop.Status = "committing";
          lockedProposals.Add(prop);
        }
      }

      var lockedSet = lockedProposals.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var activePending = pendingItems
        .Where(x => lockedSet.Contains(x.Proposal.Id))
        .Select(x => x.Proposal)
        .ToList();
      if (activePending.Count == 0)
      {
        return new AgentBatchCommitResultDto
        {
          Committed = alreadyCommittedDtos,
          RefreshServices = []
        };
      }

      var sortedProposals = AgentStepGraphHelper.SortStagedProposalsTopologically(activePending, payloadsById);
      var batchCode = Guid.NewGuid().ToString("N")[..6];
      var committedStepCount = sortedProposals.Count;
      var agentCorrelationId = committedStepCount > 1
        ? $"agent-batch:{batchCode}:{committedStepCount}"
        : $"agent:{batchCode}";
      string? committedRunId = null;

      using var correlationScope = AsyncLocalTenantContext.UseCorrelation(agentCorrelationId);

      var (stepOutcomes, allRefreshServices) = await db.RunMutationAsync(
        async mutationCt =>
        {
          committedRunId = db.CurrentMutationRunId?.ToString();

          // Phase A: Verify initial optimistic concurrency versions before any step mutates database state
          var preVerifiedEdits = new HashSet<(Type EntityType, long RecordId)>();
          foreach (var stepProp in sortedProposals)
          {
            var stepPayload = payloadsById[stepProp.Id];
            if (stepPayload.RecordId is not > 0 || !stepPayload.Ver.HasValue)
            {
              continue;
            }

            var checkFormType = ModuleCatalog.Instance.FindServiceType(stepPayload.Service);
            if (checkFormType == null ||
                ModuleCatalog.Instance.CreateServiceInstance(checkFormType, db, app) is not IFormService checkForm)
            {
              continue;
            }

            checkForm.SetRequest(new FormRequest { Id = stepPayload.RecordId });
            var existing = await checkForm.LoadParentRecordAsync(stepPayload.RecordId.Value, mutationCt);
            if (existing is IHasVersion versioned && versioned.Ver == stepPayload.Ver.Value)
            {
              preVerifiedEdits.Add((existing.GetType(), stepPayload.RecordId.Value));
            }
          }

          // Phase B: Execute steps sequentially in topological order, propagating committed IDs and intra-batch updates
          var outcomes =
            new List<(AgentProposalDto Proposal, long RecordId, string RecordLabel, string FormServicePath, List<string> RefreshServices
              )>();

          foreach (var stepProposal in sortedProposals)
          {
            var stepPayload = payloadsById[stepProposal.Id];
            var outcome = await ExecuteStepCoreAsync(
              stepProposal,
              stepPayload,
              committedIdsByProposalId,
              preVerifiedEdits,
              db,
              app,
              store,
              owner,
              L,
              mutationCt
            );

            committedIdsByProposalId[stepProposal.Id] = outcome.RecordId;
            outcomes.Add((stepProposal, outcome.RecordId, outcome.RecordLabel, outcome.FormServicePath, outcome.RefreshServices));
          }

          var refresh = outcomes
            .SelectMany(o => o.RefreshServices)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

          return (outcomes, refresh);
        },
        ct: ct
      );

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

        committedResults.Add(
          new AgentCommitResultDto
          {
            ProposalId = outcome.Proposal.Id,
            RecordId = outcome.RecordId,
            RecordLabel = outcome.RecordLabel,
            FormService = outcome.FormServicePath,
            RefreshServices = allRefreshServices,
            RunId = committedRunId,
            BatchCode = batchCode
          }
        );
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

  private static async Task<(long RecordId, string RecordLabel, string FormServicePath, List<string> RefreshServices)> ExecuteStepCoreAsync(
    AgentProposalDto proposal,
    StagedProposalPayload payload,
    Dictionary<string, long> committedIdsByProposalId,
    HashSet<(Type EntityType, long RecordId)> preVerifiedEdits,
    PlatformDbContext db,
    PlatformAppService app,
    AgentStore store,
    AgentOwnerScope owner,
    Localizer L,
    CancellationToken mutationCt
  )
  {
    var isEdit = payload.RecordId is > 0;
    var pendingTopLevelFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var pendingRef in payload.NewReferences)
    {
      long? committedRefId = null;

      if (!string.IsNullOrEmpty(pendingRef.ProposalId))
      {
        committedRefId = committedIdsByProposalId.TryGetValue(pendingRef.ProposalId, out var batchCommittedId) && batchCommittedId > 0
          ? batchCommittedId
          : store.GetProposal(pendingRef.ProposalId, owner) is { Status: "committed", CommittedRecordId: > 0 } existingCommitted
            ? existingCommitted.CommittedRecordId.Value
            : throw new InvalidOperationException(L["AgentErrorSave"]);
      }
      else if (!string.IsNullOrEmpty(pendingRef.FormService))
      {
        var refFormType = ModuleCatalog.Instance.FindServiceType(pendingRef.FormService);
        if (refFormType != null &&
            ModuleCatalog.Instance.CreateServiceInstance(refFormType, db, app) is IFormService refFormInst &&
            refFormInst.Feature.AllowsAgent &&
            AgentFeatureRegistry.CanEditFeature(refFormInst.Feature, app))
        {
          var (refEls, _) = await refFormInst.GetElementsMetadataAsync(mutationCt);
          var refDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(refFormInst, mutationCt);
          foreach (var (k, v) in pendingRef.ExtraValues)
          {
            refDefaults[k] = v;
          }

          var primaryTextPath = ResolvePrimaryTextFieldPath(refEls);
          refDefaults[primaryTextPath] = pendingRef.Name;

          var refJson = JsonSerializer.SerializeToElement(refDefaults, WebJson);
          object refSaved;
          try
          {
            refSaved = await refFormInst.SaveAsync(refJson, new FormRequest { Value = refJson }, mutationCt);
          }
          catch (FormRefusedException refusedEx)
          {
            throw new InvalidOperationException(FormatRefusedException(refusedEx, refEls, pendingRef.Name), refusedEx);
          }

          if (TryExtractSavedId(refSaved, out var refId))
          {
            committedRefId = refId;
          }
        }
      }

      if (committedRefId is > 0)
      {
        if (pendingRef.LineIndex is >= 0 and var lineIdx && lineIdx < payload.Lines.Count)
        {
          payload.Lines[lineIdx][pendingRef.FieldPath] = committedRefId.Value;
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
    }

    var formType = ModuleCatalog.Instance.FindServiceType(payload.Service)
                   ?? throw new InvalidOperationException($"Form service '{payload.Service}' not found.");

    if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, app) is not IFormService formInst ||
        !formInst.Feature.AllowsAgent ||
        !AgentFeatureRegistry.CanEditFeature(formInst.Feature, app))
    {
      throw new InvalidOperationException(L["MutationRefused"]);
    }

    var ver = payload.Ver;
    if (isEdit)
    {
      formInst.SetRequest(new FormRequest { Id = payload.RecordId });
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
          // An earlier step in this atomic batch updated this entity in-transaction:
          // adopt the fresh in-transaction version and refresh non-explicit fields so side-effects are preserved.
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
    }

    var (formElements, children) = await formInst.GetElementsMetadataAsync(mutationCt);

    var valueJson = JsonSerializer.SerializeToElement(payload.Values, WebJson);
    object saveResult;
    try
    {
      saveResult = await formInst.SaveAsync(
        valueJson,
        new FormRequest
        {
          Id = payload.RecordId,
          Ver = ver,
          Value = valueJson
        },
        mutationCt
      );
    }
    catch (FormRefusedException refusedEx)
    {
      throw new InvalidOperationException(FormatRefusedException(refusedEx, formElements), refusedEx);
    }

    if (!TryExtractSavedId(saveResult, out var recordId))
    {
      var errMsg = (saveResult as FormDataDto)?.Warnings?.FirstOrDefault()?.Detail ?? L["AgentErrorSave"];
      throw new InvalidOperationException(errMsg);
    }

    // Save child line items if present
    if ((isEdit ? payload.HasLinesUpdate : payload.Lines.Count > 0) &&
        !string.IsNullOrEmpty(payload.ChildFormService))
    {
      var childFormType = ModuleCatalog.Instance.FindServiceType(payload.ChildFormService);
      if (childFormType != null)
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(childFormType, db, app) is not IFormService metaChildForm ||
            metaChildForm.Feature.IsReadOnly ||
            !AgentFeatureRegistry.CanEditFeature(metaChildForm.Feature, app))
        {
          throw new InvalidOperationException(L["MutationRefused"]);
        }

        metaChildForm.SetRequest(
          new FormRequest
          {
            ParentService = formInst.Service,
            ParentId = recordId
          }
        );
        var (childElements, _) = await metaChildForm.GetElementsMetadataAsync(mutationCt);
        var hasSortOrderField = childElements.Any(e => string.Equals(e.Path, "sortOrder", StringComparison.OrdinalIgnoreCase));
        var childPrimaryTextKey = ResolvePrimaryTextFieldPath(childElements);

        var sortOrder = 0;
        IGridService? childGrid = null;
        var existingLineIds = new List<long>();
        var isAppendLines = string.Equals(payload.LinesMode, "append", StringComparison.OrdinalIgnoreCase);

        var resolvedChildLine = AgentFeatureRegistry.ResolveEditableChildLineGrid(children, db, app);
        if (resolvedChildLine is { } childInfo)
        {
          childGrid = childInfo.ChildGrid;
        }
        else
        {
          var fallbackLineChild = children.FirstOrDefault(c =>
            !c.IsFiles && !c.IsReadOnly && c.ServiceType != null && typeof(IGridService).IsAssignableFrom(c.ServiceType)
          );
          if (fallbackLineChild?.ServiceType != null &&
              ModuleCatalog.Instance.CreateServiceInstance(fallbackLineChild.ServiceType, db, app) is IGridService cg)
          {
            if (cg.Feature.IsReadOnly ||
                !AgentFeatureRegistry.CanViewFeature(cg.Feature, app) ||
                !AgentFeatureRegistry.CanEditFeature(cg.Feature, app))
            {
              throw new InvalidOperationException(L["MutationRefused"]);
            }

            childGrid = cg;
          }
        }

        if (isEdit && childGrid != null)
        {
          var existingPage = await childGrid.GetDataAsync(
            new GridRequest
            {
              ParentId = recordId,
              Page = 0,
              PageSize = 500,
              IsDataOnly = true
            },
            mutationCt
          );

          var maxSortOrder = 0;
          foreach (var rowObj in existingPage.Rows)
          {
            if (rowObj is IDictionary<string, object?> rowDict)
            {
              if (rowDict.TryGetValue("id", out var idObj) &&
                  TryConvertToLong(idObj, out var lid) &&
                  lid > 0)
              {
                existingLineIds.Add(lid);
              }

              if (rowDict.TryGetValue("sortOrder", out var soObj) &&
                  TryConvertToLong(soObj, out var soVal) &&
                  soVal > maxSortOrder)
              {
                maxSortOrder = (int)soVal;
              }
            }
          }

          if (isAppendLines)
          {
            sortOrder = Math.Max(existingLineIds.Count, maxSortOrder);
          }
        }

        var updatedLineIds = new HashSet<long>();
        if (isEdit)
        {
          foreach (var lineDict in payload.Lines)
          {
            if (lineDict.TryGetValue("id", out var lidObj) &&
                TryConvertToLong(lidObj, out var parsedLid) &&
                parsedLid > 0 &&
                existingLineIds.Contains(parsedLid))
            {
              updatedLineIds.Add(parsedLid);
            }
          }
        }

        if (isEdit && !isAppendLines && childGrid != null)
        {
          var idsToDelete = existingLineIds.Where(id => !updatedLineIds.Contains(id)).ToList();
          if (idsToDelete.Count > 0)
          {
            if (!childGrid.CanDelete())
            {
              throw new InvalidOperationException(L["MutationRefused"]);
            }

            if (childGrid.AllowsMultiDelete || idsToDelete.Count <= 1)
            {
              var delRes = await childGrid.DeleteAsync(
                new GridRequest
                {
                  ParentId = recordId,
                  SelectedIds = idsToDelete
                },
                mutationCt
              );
              if (delRes.Outcome != MutationOutcome.Committed)
              {
                var delErr = delRes.Warnings?.FirstOrDefault()?.Detail ?? delRes.Title ?? L["MutationRefused"];
                throw new InvalidOperationException(delErr);
              }
            }
            else
            {
              foreach (var lineId in idsToDelete)
              {
                var delRes = await childGrid.DeleteAsync(
                  new GridRequest
                  {
                    ParentId = recordId,
                    SelectedIds = [lineId]
                  },
                  mutationCt
                );
                if (delRes.Outcome != MutationOutcome.Committed)
                {
                  var delErr = delRes.Warnings?.FirstOrDefault()?.Detail ?? delRes.Title ?? L["MutationRefused"];
                  throw new InvalidOperationException(delErr);
                }
              }
            }
          }
        }

        foreach (var lineDict in payload.Lines)
        {
          sortOrder++;
          if (hasSortOrderField && (!lineDict.ContainsKey("sortOrder") || isAppendLines))
          {
            lineDict["sortOrder"] = sortOrder;
          }

          // Propagate newly committed parent reference IDs to child lines where unset
          foreach (var (k, v) in payload.Values)
          {
            if (TryConvertToLong(v, out var parentRefId) && parentRefId > 0 &&
                lineDict.TryGetValue(k, out var existingLineVal) &&
                (existingLineVal == null || existingLineVal is JsonElement je && je.ValueKind == JsonValueKind.Null))
            {
              lineDict[k] = parentRefId;
            }
          }

          long? existingLineId = isEdit &&
                                 lineDict.TryGetValue("id", out var lineIdObj) &&
                                 TryConvertToLong(lineIdObj, out var parsedLineId) &&
                                 parsedLineId > 0
            ? parsedLineId
            : null;

          if (ModuleCatalog.Instance.CreateServiceInstance(childFormType, db, app) is IFormService childFormInst)
          {
            var lineJson = JsonSerializer.SerializeToElement(lineDict, WebJson);
            try
            {
              var savedLine = await childFormInst.SaveAsync(
                lineJson,
                new FormRequest
                {
                  Id = existingLineId,
                  ParentService = formInst.Service,
                  ParentId = recordId,
                  Value = lineJson
                },
                mutationCt
              );
              if (!TryExtractSavedId(savedLine, out _))
              {
                var lineName = lineDict.TryGetValue(childPrimaryTextKey, out var nObj) ? nObj?.ToString() : $"#{sortOrder}";
                var lineErr = (savedLine as FormDataDto)?.Warnings?.FirstOrDefault()?.Detail ?? L["MutationRefused"];
                throw new InvalidOperationException($"{lineName}: {lineErr}");
              }
            }
            catch (FormRefusedException refusedEx)
            {
              var lineName = lineDict.TryGetValue(childPrimaryTextKey, out var nObj) ? nObj?.ToString() : $"#{sortOrder}";
              throw new InvalidOperationException(FormatRefusedException(refusedEx, childElements, lineName), refusedEx);
            }
          }
        }
      }
    }

    // Attach uploaded files if the form supports files
    if (proposal.AttachmentIds.Count > 0)
    {
      var fileChild = children.FirstOrDefault(c => c.IsFiles && c.ServiceType != null);
      if (fileChild?.ServiceType != null &&
          ModuleCatalog.Instance.CreateServiceInstance(fileChild.ServiceType, db, app) is IFormFilesSource fileSource)
      {
        foreach (var attId in proposal.AttachmentIds)
        {
          var att = store.GetAttachment(attId, owner);
          if (att != null && File.Exists(att.LocalPath))
          {
            var bytes = await File.ReadAllBytesAsync(att.LocalPath, mutationCt);
            _ = await fileSource.AttachFileAsync(
                  recordId,
                  att.FileName,
                  att.ContentType,
                  bytes,
                  mutationCt
                )
                ?? throw new InvalidOperationException(L["MutationRefused"]);
          }
        }
      }
    }

    var recordLabel = $"#{recordId}";
    var caption = formInst.GetRecordCaption(saveResult, recordId);
    var genericCaption = L.For(formInst.Feature.Module)["FormRecord", formInst.GetTitle(), recordId];
    var primaryTextKey = ResolvePrimaryTextFieldPath(formElements);
    if (!string.IsNullOrWhiteSpace(caption) && !string.Equals(caption, genericCaption, StringComparison.Ordinal))
    {
      recordLabel = caption.Contains($"#{recordId}", StringComparison.Ordinal)
        ? caption
        : $"{caption} (#{recordId})";
    }
    else if (payload.Values.TryGetValue(primaryTextKey, out var nameObj) &&
             nameObj is JsonElement { ValueKind: JsonValueKind.String } nameEl && !string.IsNullOrWhiteSpace(nameEl.GetString()))
    {
      recordLabel = $"{nameEl.GetString()} (#{recordId})";
    }
    else if (payload.Values.TryGetValue(primaryTextKey, out var nameStrObj) && nameStrObj is string nameStr &&
             !string.IsNullOrWhiteSpace(nameStr))
    {
      recordLabel = $"{nameStr} (#{recordId})";
    }
    else if (!string.IsNullOrWhiteSpace(proposal.Title))
    {
      recordLabel = proposal.Title.StartsWith($"#{recordId}", StringComparison.Ordinal)
        ? proposal.Title
        : $"{proposal.Title} (#{recordId})";
    }

    var refreshServices = ModuleCatalog.Instance.AllFeatures
      .Where(f => string.Equals(f.Module, formInst.Feature.Module, StringComparison.OrdinalIgnoreCase) &&
                  f.ServiceType != null &&
                  typeof(IGridService).IsAssignableFrom(f.ServiceType)
      )
      .Select(f => ServicePathResolver.Resolve(f.ServiceType!))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    return (recordId, recordLabel, formInst.Service, refreshServices);
  }
}
