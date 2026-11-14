using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentPrerequisiteReferenceStager
{
  public static async Task<List<PendingNewReference>> ResolveAndStageReferencesAsync(
    ProposalBuildContext ctx,
    IFormService formInst,
    bool isEdit,
    long? recordId,
    FormCompositeDraftDto draft,
    IReadOnlyList<FormElementDto> editableElements,
    CancellationToken ct)
  {
    var pendingRefs = new List<PendingNewReference>();
    var needsHeaderRedraft = false;

    foreach (var ur in draft.UnresolvedReferences)
    {
      if (!string.IsNullOrWhiteSpace(ur.SymbolicRefKey))
      {
        var refTarget = ctx.FindReferencedProposal(ur.SymbolicRefKey, ur.ReferenceService)
          ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
        var shortLabel = AgentStepGraphHelper.GetProposalShortLabel(refTarget);

        if (ur.ChildKey == null)
        {
          var sourceEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, ur.FieldPath, StringComparison.OrdinalIgnoreCase));
          if (refTarget.RecordId is > 0)
          {
            if (ur.IsMultiple)
            {
              var list = ExtractLongList(draft.Values.GetValueOrDefault(ur.FieldPath));
              if (!list.Contains(refTarget.RecordId.Value))
              {
                list.Add(refTarget.RecordId.Value);
              }

              draft.Values[ur.FieldPath] = list;
            }
            else
            {
              draft.Values[ur.FieldPath] = refTarget.RecordId.Value;
              needsHeaderRedraft = true;
            }

            if (!string.IsNullOrEmpty(ur.ReferenceService))
            {
              await AgentContextQueryService.EnsureReferenceIdLoadedAsync(
                ur.ReferenceService, refTarget.RecordId.Value, draft.Dictionaries, ctx.Db, ctx.App, ct);
            }
          }
          else if (sourceEl?.SelectPatches is { Count: > 0 } patches &&
                   refTarget.Payload.ValueKind == JsonValueKind.Object &&
                   refTarget.Payload.Deserialize<StagedProposalPayload>(WebJson) is { } refPayload)
          {
            foreach (var patch in patches)
            {
              if (!draft.ExplicitFields.Contains(patch.TargetField) &&
                  (refPayload.Values.TryGetValue(patch.SourceColumn, out var pv) ||
                   refPayload.Values.TryGetValue($"{patch.SourceColumn}Id", out pv)) &&
                  pv != null)
              {
                draft.Values[patch.TargetField] = pv;
                needsHeaderRedraft = true;
                var patchedEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
                if (patchedEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } pSvc } &&
                    TryConvertToLong(pv, out var pId) && pId > 0)
                {
                  await AgentContextQueryService.EnsureReferenceIdLoadedAsync(pSvc, pId, draft.Dictionaries, ctx.Db, ctx.App, ct);
                }
              }
            }
          }
        }

        pendingRefs.Add(new PendingNewReference
        {
          FieldPath = ur.FieldPath,
          Name = shortLabel,
          FormService = refTarget.Service,
          IsMultiple = ur.IsMultiple,
          ProposalId = refTarget.Id,
          StepKey = ur.SymbolicRefKey,
          StepNumber = refTarget.StepNumber,
          ChildKey = ur.ChildKey,
          LineIndex = ur.RowIndex,
          IsExistingRecordEdit = refTarget.RecordId is > 0
        });
        continue;
      }

      if (string.IsNullOrWhiteSpace(ur.CreateFormService) || string.IsNullOrWhiteSpace(ur.RawName))
      {
        throw new InvalidOperationException($"Reference '{ur.RawName}' was not found in '{ur.ReferenceService}'.");
      }

      pendingRefs.Add(new PendingNewReference
      {
        FieldPath = ur.FieldPath,
        Name = ur.RawName,
        FormService = ur.CreateFormService,
        IsMultiple = ur.IsMultiple,
        ChildKey = ur.ChildKey,
        LineIndex = ur.RowIndex,
        ExtraValues = ur.ExtraValues != null
          ? new Dictionary<string, object?>(ur.ExtraValues, StringComparer.OrdinalIgnoreCase)
          : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
      });
    }

    if (needsHeaderRedraft)
    {
      var remainingPending = pendingRefs
        .Where(nr => nr.LineIndex == null && !nr.IsExistingRecordEdit)
        .Select(nr => nr.FieldPath)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var overrides = draft.Values
        .Where(kv => !remainingPending.Contains(kv.Key))
        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
      var redrafted = await formInst.PrepareDraftAsync(isEdit ? recordId : null, overrides, remainingPending, ct: ct);
      foreach (var (k, v) in redrafted.Values)
      {
        if (!remainingPending.Contains(k) && v != null)
        {
          draft.Values[k] = v;
        }
      }

      draft.Messages.RemoveAll(m => string.IsNullOrEmpty(m.Detail) || !draft.Children.Any(cg => m.Detail.StartsWith($"{cg.Title} #", StringComparison.Ordinal)));
      draft.Messages.InsertRange(0, redrafted.Messages);
    }

    var ordered = pendingRefs
      .OrderBy(nr => nr.LineIndex.HasValue ? 1 : 0)
      .ThenBy(nr => nr.LineIndex ?? editableElements.FirstOrDefault(e => string.Equals(e.Path, nr.FieldPath, StringComparison.OrdinalIgnoreCase))?.SelectPatches?.Count ?? 0)
      .ToList();

    foreach (var pendingRef in ordered)
    {
      if (!string.IsNullOrEmpty(pendingRef.ProposalId) || string.IsNullOrEmpty(pendingRef.FormService))
      {
        continue;
      }

      var sourceEl = pendingRef.LineIndex == null
        ? editableElements.FirstOrDefault(e => string.Equals(e.Path, pendingRef.FieldPath, StringComparison.OrdinalIgnoreCase))
        : draft.Children.FirstOrDefault(cg => string.Equals(cg.ChildKey, pendingRef.ChildKey, StringComparison.OrdinalIgnoreCase))
            ?.Elements.FirstOrDefault(e => string.Equals(e.Path, pendingRef.FieldPath, StringComparison.OrdinalIgnoreCase));

      EnrichPrerequisiteExtraValues(pendingRef, sourceEl, isEdit, draft, ordered);

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

      var prereqValues = new Dictionary<string, object?>(pendingRef.ExtraValues, StringComparer.OrdinalIgnoreCase)
      {
        [primaryTextKey] = pendingRef.Name
      };
      var prereqArgs = JsonSerializer.SerializeToElement(
        new { sessionId = ctx.SessionId, service = pendingRef.FormService, values = prereqValues },
        WebJson);
      var stagedPrereq = await AgentProposalBuilder.StageProposalAsync(prereqArgs, ctx.Services, ct, turnId: ctx.TurnId);
      pendingRef.ProposalId = stagedPrereq.Id;
      pendingRef.StepNumber = stagedPrereq.StepNumber;
    }

    return ordered;
  }

  private static void EnrichPrerequisiteExtraValues(
    PendingNewReference pendingRef,
    FormElementDto? sourceEl,
    bool isEdit,
    FormCompositeDraftDto draft,
    IReadOnlyList<PendingNewReference> ordered)
  {
    if (pendingRef.LineIndex == null && sourceEl?.SelectPatches is { Count: > 0 } patches)
    {
      foreach (var patch in patches)
      {
        var sourceIdKey = $"{patch.SourceColumn}Id";
        if (draft.Values.TryGetValue(patch.TargetField, out var targetVal) && TryConvertToLong(targetVal, out var targetId) && targetId > 0)
        {
          pendingRef.ExtraValues.TryAdd(sourceIdKey, targetId);
          pendingRef.ExtraValues.TryAdd(patch.SourceColumn, targetId);
        }
        else if (ordered.FirstOrDefault(nr => nr.LineIndex == null && string.Equals(nr.FieldPath, patch.TargetField, StringComparison.OrdinalIgnoreCase)) is { } tpr)
        {
          pendingRef.ExtraValues.TryAdd(sourceIdKey, tpr.Name);
          pendingRef.ExtraValues.TryAdd(patch.SourceColumn, tpr.Name);
        }
      }
    }

    if (pendingRef.LineIndex == null && sourceEl?.Model is FormSelectModel { ReferenceDraftRules: { Count: > 0 } draftRules })
    {
      foreach (var rule in draftRules)
      {
        if ((!isEdit || draft.ExplicitFields.Contains(rule.DraftField)) &&
            draft.Values.TryGetValue(rule.DraftField, out var draftVal) && draftVal != null)
        {
          pendingRef.ExtraValues.TryAdd(rule.SourceField, draftVal);
        }
      }
    }
  }
}
