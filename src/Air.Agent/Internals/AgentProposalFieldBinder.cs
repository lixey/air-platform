using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalFieldBinder
{
  public static async Task EnsureExistingReferenceValuesLoadedAsync(
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> formValues,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
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
        foreach (var existingId in ExtractLongList(formValues.GetValueOrDefault(el.Path)))
        {
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, existingId, dictionaries, db, app, ct);
        }
      }
      else if (formValues.TryGetValue(el.Path, out var curVal) && TryConvertToLong(curVal, out var curId) && curId > 0)
      {
        await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refSvc, curId, dictionaries, db, app, ct);
      }
    }
  }

  public static async Task ParseParentFieldsAsync(
    JsonElement args,
    JsonElement valuesSource,
    bool hasNestedValues,
    bool isEdit,
    string? multiSelectMode,
    IReadOnlyList<FormElementDto> editableElements,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitlyProvided,
    List<PendingNewReference> newReferences,
    List<(FormElementDto Element, AgentProposalDto TargetProposal)> linkedSelectPatches,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Func<string, string?, AgentProposalDto?> findReferencedProposal,
    IServiceProvider services,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    foreach (var el in editableElements)
    {
      var select = el.Model as FormSelectModel;
      var hasProp = TryGetPropertyIgnoreCase(valuesSource, el.Path, out var rawProp) ||
                    hasNestedValues && TryGetPropertyIgnoreCase(args, el.Path, out rawProp);

      if (select != null && (select.IsMultiple || el.ValueKind == FormValueKind.List))
      {
        await ParseMultiSelectFieldAsync(
          el, select, args, valuesSource, hasNestedValues, hasProp, rawProp, isEdit, multiSelectMode,
          descriptors, formValues, explicitlyProvided, newReferences, dictionaries, findReferencedProposal, services, db, app, L, ct);
        continue;
      }

      if (select != null && !string.IsNullOrEmpty(select.ReferenceService))
      {
        await ParseSingleReferenceFieldAsync(
          el, select, args, valuesSource, hasNestedValues, hasProp, rawProp,
          descriptors, formValues, explicitlyProvided, newReferences, linkedSelectPatches, dictionaries, findReferencedProposal, services, db, app, L, ct);
        continue;
      }

      if (hasProp && rawProp.ValueKind != JsonValueKind.Undefined)
      {
        formValues[el.Path] = ConvertJsonValue(rawProp, el.Type);
        explicitlyProvided.Add(el.Path);
      }
    }
  }

  public static async Task ApplyLinkedSelectPatchesAsync(
    IReadOnlyList<(FormElementDto Element, AgentProposalDto TargetProposal)> linkedSelectPatches,
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitlyProvided,
    HashSet<string> pendingRefPaths,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    foreach (var (sourceEl, targetProp) in linkedSelectPatches)
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
        if (explicitlyProvided.Contains(patch.TargetField) || pendingRefPaths.Contains(patch.TargetField))
        {
          continue;
        }

        if (patch.OnlyWhenEmpty &&
            formValues.TryGetValue(patch.TargetField, out var existingTarget) &&
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
            await AgentContextQueryService.EnsureReferenceIdLoadedAsync(targetRefSvc, patchedId, dictionaries, db, app, ct);
            if (targetSel.ReferenceFilter == null ||
                !dictionaries.TryGetValue(targetRefSvc, out var tRows) ||
                FilterRowsByReferenceFilter(tRows, targetSel.ReferenceFilter).Any(r => TryGetRowId(r, out var rid) && rid == patchedId))
            {
              formValues[patch.TargetField] = patchedId;
              explicitlyProvided.Add(patch.TargetField);
            }
          }
          else if (targetEl != null && targetEl.Model is not FormSelectModel)
          {
            formValues[patch.TargetField] = unwrapped;
            explicitlyProvided.Add(patch.TargetField);
          }
        }
      }
    }
  }

  private static async Task ParseMultiSelectFieldAsync(
    FormElementDto el,
    FormSelectModel select,
    JsonElement args,
    JsonElement valuesSource,
    bool hasNestedValues,
    bool hasProp,
    JsonElement rawProp,
    bool isEdit,
    string? multiSelectMode,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitlyProvided,
    List<PendingNewReference> newReferences,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Func<string, string?, AgentProposalDto?> findReferencedProposal,
    IServiceProvider services,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    if (!hasProp && el.Path.EndsWith("Ids", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 3)
    {
      var baseMultiName = el.Path[..^3];
      foreach (var multiAlias in new[] { $"{baseMultiName}s", baseMultiName })
      {
        if (TryGetPropertyIgnoreCase(valuesSource, multiAlias, out rawProp) ||
            hasNestedValues && TryGetPropertyIgnoreCase(args, multiAlias, out rawProp))
        {
          hasProp = true;
          break;
        }
      }
    }

    if (hasProp && rawProp.ValueKind == JsonValueKind.Array)
    {
      var ids = isEdit && string.Equals(multiSelectMode, "append", StringComparison.OrdinalIgnoreCase)
        ? ExtractLongList(formValues.GetValueOrDefault(el.Path))
        : new List<long>();
      var refFormDesc = !string.IsNullOrEmpty(select.ReferenceService)
        ? AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, select.ReferenceService, el, services, db, app)
        : null;

      foreach (var item in rawProp.EnumerateArray())
      {
        if (AgentStepGraphHelper.TryExtractRefKey(item, out var multiRefKey))
        {
          var multiTarget = findReferencedProposal(multiRefKey, select.ReferenceService)
            ?? throw new InvalidOperationException(L["MutationRefused"]);
          newReferences.Add(new PendingNewReference
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
          dictionaries,
          db,
          app,
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
          newReferences.Add(new PendingNewReference
          {
            FieldPath = el.Path,
            Name = unresolvedItemName,
            FormService = refFormDesc.ServicePath,
            IsMultiple = true,
            ExtraValues = ExtractEqualityFilterMap(select.ReferenceFilter)
          });
        }
      }

      formValues[el.Path] = ids;
      explicitlyProvided.Add(el.Path);
    }
    else if (hasProp && rawProp.ValueKind == JsonValueKind.Null)
    {
      formValues[el.Path] = new List<long>();
      explicitlyProvided.Add(el.Path);
    }
  }

  private static async Task ParseSingleReferenceFieldAsync(
    FormElementDto el,
    FormSelectModel select,
    JsonElement args,
    JsonElement valuesSource,
    bool hasNestedValues,
    bool hasProp,
    JsonElement rawProp,
    IReadOnlyList<GridCreateDescriptor> descriptors,
    Dictionary<string, object?> formValues,
    HashSet<string> explicitlyProvided,
    List<PendingNewReference> newReferences,
    List<(FormElementDto Element, AgentProposalDto TargetProposal)> linkedSelectPatches,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Func<string, string?, AgentProposalDto?> findReferencedProposal,
    IServiceProvider services,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    var refFormDesc = AgentFeatureRegistry.ResolveReferenceCreateForm(descriptors, select.ReferenceService!, el, services, db, app);
    if (!hasProp && el.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && el.Path.Length > 2)
    {
      var baseRefName = el.Path[..^2];
      foreach (var refAlias in new[] { $"{baseRefName}Name", baseRefName })
      {
        if (TryGetPropertyIgnoreCase(valuesSource, refAlias, out rawProp) ||
            hasNestedValues && TryGetPropertyIgnoreCase(args, refAlias, out rawProp))
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
      var singleTarget = findReferencedProposal(singleRefKey, select.ReferenceService)
        ?? throw new InvalidOperationException(L["MutationRefused"]);
      formValues[el.Path] = null;
      explicitlyProvided.Add(el.Path);
      newReferences.Add(new PendingNewReference
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
        linkedSelectPatches.Add((el, singleTarget));
      }

      return;
    }

    var (resolvedId, unresolvedName, objectExtraValues) = await AgentReferenceResolver.ResolveReferenceValueAsync(
      rawProp,
      select.ReferenceService!,
      select.ReferenceFilter,
      allowFuzzy: refFormDesc == null,
      allowWrongKindFallback: true,
      dictionaries,
      db,
      app,
      ct,
      select.ReferenceLabelPaths);

    if (resolvedId.HasValue)
    {
      formValues[el.Path] = resolvedId.Value;
      explicitlyProvided.Add(el.Path);
    }
    else if (!string.IsNullOrWhiteSpace(unresolvedName))
    {
      formValues[el.Path] = null;
      explicitlyProvided.Add(el.Path);
      var extra = ExtractEqualityFilterMap(select.ReferenceFilter);
      if (objectExtraValues != null)
      {
        foreach (var (k, v) in objectExtraValues)
        {
          extra[k] = v;
        }
      }

      newReferences.Add(new PendingNewReference
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
      formValues[el.Path] = null;
      explicitlyProvided.Add(el.Path);
    }
  }
}
