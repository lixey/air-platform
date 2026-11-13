using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalFieldBinder
{
  public static async Task EnsureExistingReferenceValuesLoadedAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    IReadOnlyList<FormElementDto> editableElements,
    CancellationToken ct)
  {
    foreach (var el in editableElements)
    {
      if (el.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } sel)
      {
        continue;
      }

      if (sel.IsMultiple || el.ValueKind == FormValueKind.List)
      {
        foreach (var existingId in ExtractLongList(draft.FormValues.GetValueOrDefault(el.Path)))
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, existingId, draft.Dictionaries, ctx.Db, ctx.App, ct);
        }
      }
      else if (draft.FormValues.TryGetValue(el.Path, out var curVal) && TryConvertToLong(curVal, out var curId) && curId > 0)
      {
        await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, curId, draft.Dictionaries, ctx.Db, ctx.App, ct);
      }
    }
  }

  public static async Task ParseParentFieldsAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    IReadOnlyList<FormElementDto> editableElements,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    CancellationToken ct)
  {
    foreach (var el in editableElements)
    {
      var select = el.Model as FormSelectModel;
      if (select != null && (select.IsMultiple || el.ValueKind == FormValueKind.List))
      {
        await ParseMultiSelectFieldAsync(ctx, draft, el, select, descriptors, ct);
        continue;
      }

      if (select != null && !string.IsNullOrEmpty(select.ReferenceService))
      {
        await ParseSingleReferenceFieldAsync(ctx, draft, el, select, descriptors, ct);
        continue;
      }

      if (draft.TryReadInputProperty(el.Path, out var rawProp) && rawProp.ValueKind != JsonValueKind.Undefined)
      {
        draft.FormValues[el.Path] = ConvertJsonValue(rawProp, el.Type);
        draft.ExplicitlyProvided.Add(el.Path);
      }
    }
  }

  public static async Task ApplyLinkedSelectPatchesAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    IReadOnlyList<FormElementDto> editableElements,
    HashSet<string> pendingRefPaths,
    CancellationToken ct)
  {
    foreach (var (sourceEl, targetProp) in draft.LinkedSelectPatches)
    {
      if (sourceEl.SelectPatches is not { Count: > 0 } patches || targetProp.Payload.ValueKind != JsonValueKind.Object)
      {
        continue;
      }

      var targetPayload = targetProp.Payload.Deserialize<StagedProposalPayload>(WebJson);
      if (targetPayload == null)
      {
        continue;
      }

      foreach (var patch in patches)
      {
        if (draft.ExplicitlyProvided.Contains(patch.TargetField) || pendingRefPaths.Contains(patch.TargetField))
        {
          continue;
        }

        if (patch.OnlyWhenEmpty &&
            draft.FormValues.TryGetValue(patch.TargetField, out var existingTarget) &&
            existingTarget != null &&
            !(TryConvertToDecimal(existingTarget, out var existingDec) && existingDec == 0m) &&
            existingTarget.ToString() is { Length: > 0 } s && s != "0")
        {
          continue;
        }

        var sourceIdKey = $"{patch.SourceColumn}Id";
        if ((targetPayload.Values.TryGetValue(sourceIdKey, out var patchedVal) ||
             targetPayload.Values.TryGetValue(patch.SourceColumn, out patchedVal)) &&
            patchedVal != null &&
            UnwrapReferenceValue(patchedVal) is { } unwrapped)
        {
          var targetEl = editableElements.FirstOrDefault(e => string.Equals(e.Path, patch.TargetField, StringComparison.OrdinalIgnoreCase));
          if (targetEl?.Model is FormSelectModel { ReferenceService: { Length: > 0 } targetRefSvc } targetSel &&
              TryConvertToLong(unwrapped, out var patchedId) && patchedId > 0)
          {
            await AgentContextQueryService.EnsureReferenceIdLoadedAsync(targetRefSvc, patchedId, draft.Dictionaries, ctx.Db, ctx.App, ct);
            if (targetSel.ReferenceFilter == null ||
                !draft.Dictionaries.TryGetValue(targetRefSvc, out var tRows) ||
                FilterRowsByReferenceFilter(tRows, targetSel.ReferenceFilter).Any(r => TryGetRowId(r, out var rid) && rid == patchedId))
            {
              draft.FormValues[patch.TargetField] = patchedId;
              draft.ExplicitlyProvided.Add(patch.TargetField);
            }
          }
          else if (targetEl != null && targetEl.Model is not FormSelectModel)
          {
            draft.FormValues[patch.TargetField] = unwrapped;
            draft.ExplicitlyProvided.Add(patch.TargetField);
          }
        }
      }
    }
  }

  private static async Task ParseMultiSelectFieldAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    FormElementDto el,
    FormSelectModel select,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    CancellationToken ct)
  {
    var hasProp = draft.TryReadInputProperty(el.Path, out var rawProp);
    if (!hasProp && el.Path.EndsWith("Ids", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 3)
    {
      var baseMultiName = el.Path[..^3];
      foreach (var multiAlias in new[] { $"{baseMultiName}s", baseMultiName })
      {
        if (draft.TryReadInputProperty(multiAlias, out rawProp))
        {
          hasProp = true;
          break;
        }
      }
    }

    if (hasProp && rawProp.ValueKind == JsonValueKind.Array)
    {
      var ids = draft.IsEdit && string.Equals(draft.MultiSelectMode, "append", StringComparison.OrdinalIgnoreCase)
        ? ExtractLongList(draft.FormValues.GetValueOrDefault(el.Path))
        : new List<long>();
      var refFormDesc = !string.IsNullOrEmpty(select.ReferenceService)
        ? AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, select.ReferenceService, el, ctx.Services, ctx.Db, ctx.App)
        : null;

      foreach (var item in rawProp.EnumerateArray())
      {
        if (AgentStepGraphHelper.TryExtractRefKey(item, out var multiRefKey))
        {
          var multiTarget = ctx.FindReferencedProposal(multiRefKey, select.ReferenceService)
            ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
          draft.NewReferences.Add(new PendingNewReference
          {
            FieldPath = el.Path,
            Name = AgentStepGraphHelper.GetProposalShortLabel(multiTarget),
            FormService = multiTarget.Service,
            IsMultiple = true,
            ProposalId = multiTarget.Id,
            StepKey = multiRefKey,
            StepNumber = multiTarget.StepNumber,
            IsExistingRecordEdit = multiTarget.RecordId is > 0
          });
          continue;
        }

        var (itemId, unresolvedItemName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
          item,
          select.ReferenceService ?? string.Empty,
          select.ReferenceFilter,
          allowFuzzy: refFormDesc == null,
          allowWrongKindFallback: false,
          draft.Dictionaries,
          ctx.Db,
          ctx.App,
          ct,
          select.ReferenceLabelPaths);
        if (itemId is > 0 and var id)
        {
          if (!ids.Contains(id))
          {
            ids.Add(id);
          }
        }
        else if (!string.IsNullOrWhiteSpace(unresolvedItemName) && refFormDesc != null)
        {
          draft.NewReferences.Add(new PendingNewReference
          {
            FieldPath = el.Path,
            Name = unresolvedItemName,
            FormService = refFormDesc.ServicePath,
            IsMultiple = true,
            ExtraValues = ExtractEqualityFilterMap(select.ReferenceFilter)
          });
        }
      }

      draft.FormValues[el.Path] = ids;
      draft.ExplicitlyProvided.Add(el.Path);
    }
    else if (hasProp && rawProp.ValueKind == JsonValueKind.Null)
    {
      draft.FormValues[el.Path] = new List<long>();
      draft.ExplicitlyProvided.Add(el.Path);
    }
  }

  private static async Task ParseSingleReferenceFieldAsync(
    ProposalBuildContext ctx,
    ProposalDraftState draft,
    FormElementDto el,
    FormSelectModel select,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    CancellationToken ct)
  {
    var refFormDesc = AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, select.ReferenceService!, el, ctx.Services, ctx.Db, ctx.App);
    var hasProp = draft.TryReadInputProperty(el.Path, out var rawProp);
    if (!hasProp && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
    {
      var baseRefName = el.Path[..^2];
      foreach (var refAlias in new[] { $"{baseRefName}Name", baseRefName })
      {
        if (draft.TryReadInputProperty(refAlias, out rawProp))
        {
          hasProp = true;
          break;
        }
      }
    }

    if (!hasProp)
    {
      return;
    }

    if (AgentStepGraphHelper.TryExtractRefKey(rawProp, out var singleRefKey))
    {
      var singleTarget = ctx.FindReferencedProposal(singleRefKey, select.ReferenceService)
        ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
      draft.FormValues[el.Path] = null;
      draft.ExplicitlyProvided.Add(el.Path);
      draft.NewReferences.Add(new PendingNewReference
      {
        FieldPath = el.Path,
        Name = AgentStepGraphHelper.GetProposalShortLabel(singleTarget),
        FormService = singleTarget.Service,
        IsMultiple = false,
        ProposalId = singleTarget.Id,
        StepKey = singleRefKey,
        StepNumber = singleTarget.StepNumber,
        IsExistingRecordEdit = singleTarget.RecordId is > 0
      });
      if (el.SelectPatches is { Count: > 0 })
      {
        draft.LinkedSelectPatches.Add((el, singleTarget));
      }

      return;
    }

    var (resolvedId, unresolvedName, objectExtraValues) = await AgentReferenceResolver.ResolveReferenceValueAsync(
      rawProp,
      select.ReferenceService!,
      select.ReferenceFilter,
      allowFuzzy: refFormDesc == null,
      allowWrongKindFallback: true,
      draft.Dictionaries,
      ctx.Db,
      ctx.App,
      ct,
      select.ReferenceLabelPaths);

    if (resolvedId.HasValue)
    {
      draft.FormValues[el.Path] = resolvedId.Value;
      draft.ExplicitlyProvided.Add(el.Path);
    }
    else if (!string.IsNullOrWhiteSpace(unresolvedName))
    {
      draft.FormValues[el.Path] = null;
      draft.ExplicitlyProvided.Add(el.Path);
      var extra = ExtractEqualityFilterMap(select.ReferenceFilter);
      if (objectExtraValues != null)
      {
        foreach (var (k, v) in objectExtraValues)
        {
          extra[k] = v;
        }
      }

      draft.NewReferences.Add(new PendingNewReference
      {
        FieldPath = el.Path,
        Name = unresolvedName,
        FormService = refFormDesc?.ServicePath,
        IsMultiple = false,
        ExtraValues = extra
      });
    }
    else if (rawProp.ValueKind == JsonValueKind.Null)
    {
      draft.FormValues[el.Path] = null;
      draft.ExplicitlyProvided.Add(el.Path);
    }
  }
}
