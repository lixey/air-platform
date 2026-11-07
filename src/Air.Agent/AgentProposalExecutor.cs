using Platform.Agent.Internals;
using Platform.App.Files.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentProposalExecutor
{
  public static async Task<AgentCommitResultDto> CommitProposalAsync(
    AgentProposalDto proposal,
    IServiceProvider services,
    CancellationToken ct)
  {
    var db = services.GetRequiredService<PlatformDbContext>();
    var app = services.GetRequiredService<PlatformAppService>();
    var store = services.GetRequiredService<AgentStore>();
    var L = Localizer.Instance;
    var owner = AgentStore.ResolveOwner(services);

    lock (proposal)
    {
      if (proposal.Status == "committed" && proposal.CommittedRecordId.HasValue)
      {
        return new AgentCommitResultDto
        {
          ProposalId = proposal.Id,
          RecordId = proposal.CommittedRecordId.Value,
          RecordLabel = proposal.CommittedLabel ?? $"#{proposal.CommittedRecordId.Value}",
          FormService = proposal.FormService ?? proposal.Service,
          RefreshServices = []
        };
      }

      if (proposal.Status == "committing")
      {
        throw new InvalidOperationException(L["MutationRefused"]);
      }

      proposal.Status = "committing";
    }

    var alsoCommitted = new List<AgentCommitResultDto>();
    try
    {
      return await db.RunMutationAsync(async mutationCt =>
      {
        var payload = proposal.Payload.Deserialize<StagedProposalPayload>(WebJson)
          ?? throw new InvalidOperationException(L["AgentErrorSave"]);

        var isEdit = payload.RecordId is > 0;

        // Resolve or commit any prerequisite new-entity references first
        foreach (var pendingRef in payload.NewReferences)
        {
          if (string.IsNullOrEmpty(pendingRef.FormService))
          {
            continue;
          }

          AgentProposalDto? prereqProposal = null;
          if (!string.IsNullOrEmpty(pendingRef.ProposalId))
          {
            prereqProposal = store.GetProposal(pendingRef.ProposalId, owner);
          }

          prereqProposal ??= store.GetSessionProposals(proposal.SessionId, owner).FirstOrDefault(p =>
            p.Id != proposal.Id &&
            string.Equals(p.Service, pendingRef.FormService, StringComparison.OrdinalIgnoreCase) &&
            p.Title.Contains(pendingRef.Name, StringComparison.OrdinalIgnoreCase));

          long? committedRefId = null;

          if (prereqProposal != null)
          {
            if (prereqProposal.Status == "committed" && prereqProposal.CommittedRecordId is > 0)
            {
              committedRefId = prereqProposal.CommittedRecordId.Value;
            }
            else
            {
              var prereqResult = await CommitProposalAsync(prereqProposal, services, mutationCt);
              if (prereqResult.AlsoCommitted is { Count: > 0 })
              {
                foreach (var nested in prereqResult.AlsoCommitted)
                {
                  if (alsoCommitted.All(a => a.ProposalId != nested.ProposalId))
                  {
                    alsoCommitted.Add(nested);
                  }
                }
              }

              if (alsoCommitted.All(a => a.ProposalId != prereqResult.ProposalId))
              {
                alsoCommitted.Add(prereqResult);
              }

              committedRefId = prereqResult.RecordId;
            }
          }
          else
          {
            // Fallback if prerequisite proposal wasn't staged separately
            var refFormType = ModuleCatalog.Instance.FindServiceType(pendingRef.FormService);
            if (refFormType != null &&
                ModuleCatalog.Instance.CreateServiceInstance(refFormType, db, app) is IFormService refFormInst &&
                refFormInst.Feature.AllowsAgent &&
                AgentFeatureRegistry.CanEditFeature(refFormInst.Feature, app))
            {
              var (refEls, _) = await refFormInst.GetElementsMetadataAsync(mutationCt);
              var refDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(
                refFormInst,
                mutationCt);
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
          ver ??= (existingEntity as IHasVersion)?.Ver;
        }

        var (formElements, children) = await formInst.GetElementsMetadataAsync(mutationCt);

        var valueJson = JsonSerializer.SerializeToElement(payload.Values, WebJson);
        object saveResult;
        try
        {
          saveResult = await formInst.SaveAsync(valueJson, new FormRequest
          {
            Id = payload.RecordId,
            Ver = ver,
            Value = valueJson
          }, mutationCt);
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

            metaChildForm.SetRequest(new FormRequest
            {
              ParentService = formInst.Service,
              ParentId = recordId
            });
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
                !c.IsFiles && !c.IsReadOnly && c.ServiceType != null && typeof(IGridService).IsAssignableFrom(c.ServiceType));
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
              var existingPage = await childGrid.GetDataAsync(new GridRequest
              {
                ParentId = recordId,
                Page = 0,
                PageSize = 500,
                IsDataOnly = true
              }, mutationCt);

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
                  var delRes = await childGrid.DeleteAsync(new GridRequest
                  {
                    ParentId = recordId,
                    SelectedIds = idsToDelete
                  }, mutationCt);
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
                    var delRes = await childGrid.DeleteAsync(new GridRequest
                    {
                      ParentId = recordId,
                      SelectedIds = [lineId]
                    }, mutationCt);
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
                  var savedLine = await childFormInst.SaveAsync(lineJson, new FormRequest
                  {
                    Id = existingLineId,
                    ParentService = formInst.Service,
                    ParentId = recordId,
                    Value = lineJson
                  }, mutationCt);
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
                var attached = await fileSource.AttachFileAsync(recordId, att.FileName, att.ContentType, bytes, mutationCt) ?? throw new InvalidOperationException(L["MutationRefused"]);
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
        else if (payload.Values.TryGetValue(primaryTextKey, out var nameObj) && nameObj is JsonElement { ValueKind: JsonValueKind.String } nameEl && !string.IsNullOrWhiteSpace(nameEl.GetString()))
        {
          recordLabel = $"{nameEl.GetString()} (#{recordId})";
        }
        else if (payload.Values.TryGetValue(primaryTextKey, out var nameStrObj) && nameStrObj is string nameStr && !string.IsNullOrWhiteSpace(nameStr))
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
                      typeof(IGridService).IsAssignableFrom(f.ServiceType))
          .Select(f => ServicePathResolver.Resolve(f.ServiceType!))
          .Concat(alsoCommitted.SelectMany(a => a.RefreshServices))
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToList();

        lock (proposal)
        {
          proposal.Status = "committed";
          proposal.CommittedRecordId = recordId;
          proposal.CommittedLabel = recordLabel;
          proposal.FormService = formInst.Service;
        }

        return new AgentCommitResultDto
        {
          ProposalId = proposal.Id,
          RecordId = recordId,
          RecordLabel = recordLabel,
          FormService = formInst.Service,
          RefreshServices = refreshServices,
          AlsoCommitted = alsoCommitted.Count > 0 ? alsoCommitted : null
        };
      }, ct: ct);
    }
    catch
    {
      lock (proposal)
      {
        proposal.Status = "pending";
        proposal.CommittedRecordId = null;
      }

      foreach (var item in alsoCommitted)
      {
        var prereq = store.GetProposal(item.ProposalId, owner);
        if (prereq != null)
        {
          lock (prereq)
          {
            prereq.Status = "pending";
            prereq.CommittedRecordId = null;
          }
        }
      }

      throw;
    }
  }
}
