using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static partial class AgentProposalBuilder
{
  private static async Task<(List<AgentProposalLineDto> ProposalLines, List<Dictionary<string, object?>> RawLineDicts)>
    ParseChildLinesAsync(
      JsonElement linesProp,
      string linesMode,
      bool isEdit,
      long? recordId,
      GridCreateDescriptor targetDesc,
      ChildGridBuilder childBuilder,
      IGridService? childGrid,
      IFormService childFormInst,
      IReadOnlyList<FormElementDto> childEditableElements,
      Dictionary<string, object?> formValues,
      HashSet<string> explicitlyProvided,
      List<PendingNewReference> newReferences,
      Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
      AgentStore store,
      string sessionId,
      AgentOwnerScope owner,
      string? turnId,
      PlatformDbContext db,
      PlatformAppService app,
      Localizer L,
      CancellationToken ct
    )
  {
    var proposalLines = new List<AgentProposalLineDto>();
    var rawLineDicts = new List<Dictionary<string, object?>>();

    if (isEdit &&
        childGrid != null &&
        !string.Equals(linesMode, "append", StringComparison.OrdinalIgnoreCase) &&
        !childGrid.CanDelete())
    {
      var allHaveIds = linesProp.GetArrayLength() > 0 &&
                       linesProp.EnumerateArray().All(el => el.ValueKind == JsonValueKind.Object && TryGetRecordId(el) is > 0);
      if (!allHaveIds)
      {
        var countDto = await childGrid.GetCountAsync(new GridRequest { ParentId = recordId }, ct);
        if (countDto.Total > 0)
        {
          throw new InvalidOperationException(L["MutationRefused"]);
        }
      }
    }

    var childDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(childFormInst, ct);
    var qtyElement = childEditableElements.FirstOrDefault(e =>
      e.Path.Equals("qty", StringComparison.OrdinalIgnoreCase) ||
      e.Path.Equals("quantity", StringComparison.OrdinalIgnoreCase));
    var priceElement = childEditableElements.FirstOrDefault(e => e.Path.Equals("price", StringComparison.OrdinalIgnoreCase));
    var amountElement = childEditableElements.FirstOrDefault(e => e.Path.Equals("amount", StringComparison.OrdinalIgnoreCase));

    foreach (var item in linesProp.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.Object)
      {
        continue;
      }

      var itemLineId = isEdit ? TryGetRecordId(item) : null;
      var baseLineValues = itemLineId is > 0
        ? (await childFormInst.PrepareDraftAsync(
          recordId: itemLineId.Value,
          parentId: recordId,
          parentService: targetDesc.ServicePath,
          ct: ct
        )).Values
        : childDefaults;

      var lineDict = new Dictionary<string, object?>(baseLineValues, StringComparer.OrdinalIgnoreCase);
      if (itemLineId is > 0)
      {
        lineDict["id"] = itemLineId.Value;
      }

      var lineRefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      var explicitlySetLineFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var linePendingRefs = new List<PendingNewReference>();

      foreach (var cel in childEditableElements)
      {
        var hasCellProp = TryGetPropertyIgnoreCase(item, cel.Path, out var rawCellProp);
        if (!hasCellProp && qtyElement != null && ReferenceEquals(cel, qtyElement))
        {
          var altQtyKey = cel.Path.Equals("qty", StringComparison.OrdinalIgnoreCase) ? "quantity" : "qty";
          hasCellProp = TryGetPropertyIgnoreCase(item, altQtyKey, out rawCellProp);
        }

        if (cel.Model is FormSelectModel cSelect && !string.IsNullOrEmpty(cSelect.ReferenceService))
        {
          long? resolvedCellId = null;
          string? resolvedCellName = null;
          var handledByLineStepRef = false;

          if (hasCellProp)
          {
            explicitlySetLineFields.Add(cel.Path);
            if (AgentStepGraphHelper.TryExtractRefKey(rawCellProp, out var cellRefKey))
            {
              handledByLineStepRef = true;
              var cellTarget = FindReferencedProposal(
                                 store,
                                 sessionId,
                                 owner,
                                 turnId,
                                 cellRefKey,
                                 cSelect.ReferenceService
                               )
                               ?? throw new InvalidOperationException(L["MutationRefused"]);
              var cellTargetLabel = AgentStepGraphHelper.GetProposalShortLabel(cellTarget);
              if (cellTarget.RecordId is > 0)
              {
                resolvedCellId = cellTarget.RecordId.Value;
                resolvedCellName = cellTargetLabel;
              }
              else
              {
                lineRefLabels[cel.Path] = cellTargetLabel;
              }

              linePendingRefs.Add(
                new PendingNewReference
                {
                  FieldPath = cel.Path,
                  Name = cellTargetLabel,
                  FormService = cellTarget.Service,
                  ProposalId = cellTarget.Id,
                  StepKey = cellRefKey,
                  StepNumber = cellTarget.StepNumber,
                  IsExistingRecordEdit = cellTarget.RecordId is > 0
                }
              );
            }
            else
            {
              (resolvedCellId, resolvedCellName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
                rawCellProp,
                cSelect.ReferenceService,
                cSelect.ReferenceFilter,
                allowFuzzy: true,
                allowWrongKindFallback: false,
                dictionaries,
                db,
                app,
                ct,
                cSelect.ReferenceLabelPaths
              );
            }
          }

          if (!handledByLineStepRef && resolvedCellId == null && cel.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
          {
            var baseName = cel.Path[..^2];
            var isPrimaryRefWithoutNameField =
              !childEditableElements.Any(e => string.Equals(e.Path, "name", StringComparison.OrdinalIgnoreCase)) &&
              ReferenceEquals(cel, childEditableElements.FirstOrDefault(e => e.Model is FormSelectModel));
            var aliases = isPrimaryRefWithoutNameField
              ? new[] { $"{baseName}Name", baseName, "name" }
              : new[] { $"{baseName}Name", baseName };
            foreach (var alias in aliases)
            {
              if (TryGetPropertyIgnoreCase(item, alias, out var aliasProp))
              {
                explicitlySetLineFields.Add(cel.Path);
                if (AgentStepGraphHelper.TryExtractRefKey(aliasProp, out var aliasCellRefKey))
                {
                  handledByLineStepRef = true;
                  var aliasCellTarget = FindReferencedProposal(
                                          store,
                                          sessionId,
                                          owner,
                                          turnId,
                                          aliasCellRefKey,
                                          cSelect.ReferenceService
                                        )
                                        ?? throw new InvalidOperationException(L["MutationRefused"]);
                  var aliasTargetLabel = AgentStepGraphHelper.GetProposalShortLabel(aliasCellTarget);
                  if (aliasCellTarget.RecordId is > 0)
                  {
                    resolvedCellId = aliasCellTarget.RecordId.Value;
                    resolvedCellName = aliasTargetLabel;
                  }
                  else
                  {
                    lineRefLabels[cel.Path] = aliasTargetLabel;
                  }

                  linePendingRefs.Add(
                    new PendingNewReference
                    {
                      FieldPath = cel.Path,
                      Name = aliasTargetLabel,
                      FormService = aliasCellTarget.Service,
                      ProposalId = aliasCellTarget.Id,
                      StepKey = aliasCellRefKey,
                      StepNumber = aliasCellTarget.StepNumber,
                      IsExistingRecordEdit = aliasCellTarget.RecordId is > 0
                    }
                  );

                  break;
                }

                (resolvedCellId, resolvedCellName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
                  aliasProp,
                  cSelect.ReferenceService,
                  cSelect.ReferenceFilter,
                  allowFuzzy: true,
                  allowWrongKindFallback: false,
                  dictionaries,
                  db,
                  app,
                  ct,
                  cSelect.ReferenceLabelPaths
                );
                if (resolvedCellId != null)
                {
                  break;
                }
              }
            }
          }

          if (!handledByLineStepRef &&
              resolvedCellId == null &&
              formValues.TryGetValue(cel.Path, out var parentRefVal) &&
              TryConvertToLong(parentRefVal, out var parentRefId) &&
              parentRefId > 0)
          {
            resolvedCellId = parentRefId;
          }

          lineDict[cel.Path] = resolvedCellId;

          if (resolvedCellId is > 0)
          {
            var refLabel = !string.IsNullOrWhiteSpace(resolvedCellName)
              ? resolvedCellName
              : ResolveSingleName(resolvedCellId.Value, cSelect, dictionaries);
            if (!string.IsNullOrWhiteSpace(refLabel))
            {
              lineRefLabels[cel.Path] = refLabel;
            }
          }
          else if (!lineRefLabels.ContainsKey(cel.Path) &&
                   newReferences.FirstOrDefault(nr =>
                     nr.LineIndex == null && string.Equals(nr.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)
                   ) is { } pendingParentRef)
          {
            lineRefLabels[cel.Path] = pendingParentRef.Name;
          }

          continue;
        }

        if (hasCellProp && rawCellProp.ValueKind != JsonValueKind.Undefined)
        {
          explicitlySetLineFields.Add(cel.Path);
          lineDict[cel.Path] = ConvertJsonValue(rawCellProp, cel.Type);
        }
      }

      // Delegate all line defaults, SelectPatches, and hooks.Compute to FormService.PrepareDraftAsync
      var linePendingPaths = linePendingRefs
        .Where(r => !r.IsExistingRecordEdit)
        .Select(r => r.FieldPath)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      var lineOverrides = explicitlySetLineFields
        .Where(k => !linePendingPaths.Contains(k))
        .ToDictionary(
          k => k,
          k => lineDict.GetValueOrDefault(k),
          StringComparer.OrdinalIgnoreCase
        );
      var lineDraft = await childFormInst.PrepareDraftAsync(
        recordId: itemLineId,
        overrides: lineOverrides,
        pendingReferencePaths: linePendingPaths,
        parentId: isEdit ? recordId : null,
        parentService: targetDesc.ServicePath,
        ct: ct
      );
      foreach (var (k, v) in lineDraft.Values)
      {
        if (!linePendingPaths.Contains(k) && v != null &&
            (!explicitlySetLineFields.Contains(k) ||
             !lineDict.TryGetValue(k, out var curV) ||
             curV == null ||
             TryConvertToDecimal(curV, out var curDec) && curDec == 0m))
        {
          lineDict[k] = v;
        }
      }

      var usedLinePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sortOrder" };
      string? name = null;
      if (lineDict.TryGetValue("name", out var nameObj) && !string.IsNullOrWhiteSpace(nameObj?.ToString()))
      {
        name = nameObj.ToString()!.Trim();
        usedLinePaths.Add("name");
      }

      if (string.IsNullOrWhiteSpace(name))
      {
        var firstTextEl = childEditableElements.FirstOrDefault(e => e.Type == UiFieldType.Text && e.Model is not FormSelectModel);
        if (firstTextEl != null && lineDict.TryGetValue(firstTextEl.Path, out var altObj) && !string.IsNullOrWhiteSpace(altObj?.ToString()))
        {
          name = altObj.ToString()!.Trim();
          usedLinePaths.Add(firstTextEl.Path);
        }
      }

      if (string.IsNullOrWhiteSpace(name))
      {
        var firstRefEl = childEditableElements.FirstOrDefault(e => e.Model is FormSelectModel && lineRefLabels.ContainsKey(e.Path));
        if (firstRefEl != null && lineRefLabels.TryGetValue(firstRefEl.Path, out var refLabel))
        {
          name = refLabel;
          usedLinePaths.Add(firstRefEl.Path);
        }
      }

      if (string.IsNullOrWhiteSpace(name))
      {
        continue;
      }

      string? lineCategoryLabel = null;
      var secondaryRefEl = childEditableElements.FirstOrDefault(e =>
        e.Model is FormSelectModel && !usedLinePaths.Contains(e.Path) && lineRefLabels.ContainsKey(e.Path)
      );
      if (secondaryRefEl != null && lineRefLabels.TryGetValue(secondaryRefEl.Path, out var secLabel))
      {
        lineCategoryLabel = secLabel;
        usedLinePaths.Add(secondaryRefEl.Path);
      }

      decimal? lineQty = null;
      if (qtyElement != null &&
          lineDict.TryGetValue(qtyElement.Path, out var rawQty) &&
          TryConvertToDecimal(rawQty, out var decQty) &&
          decQty != 0m)
      {
        lineQty = decQty;
        usedLinePaths.Add(qtyElement.Path);
      }

      decimal? linePrice = null;
      if (priceElement != null &&
          lineDict.TryGetValue(priceElement.Path, out var rawPrice) &&
          TryConvertToDecimal(rawPrice, out var decPrice) &&
          decPrice != 0m)
      {
        linePrice = decPrice;
        usedLinePaths.Add(priceElement.Path);
      }

      decimal? lineAmount = null;
      if (amountElement != null &&
          lineDict.TryGetValue(amountElement.Path, out var rawAmt) &&
          TryConvertToDecimal(rawAmt, out var decAmt) &&
          decAmt != 0m)
      {
        lineAmount = decAmt;
        usedLinePaths.Add(amountElement.Path);
      }

      List<AgentProposalFieldDto>? extraLineFields = null;
      foreach (var cel in childEditableElements)
      {
        if (usedLinePaths.Contains(cel.Path) || !explicitlySetLineFields.Contains(cel.Path))
        {
          continue;
        }

        string? formattedVal = null;
        if (lineRefLabels.TryGetValue(cel.Path, out var extraRefLabel))
        {
          formattedVal = extraRefLabel;
        }
        else if (lineDict.TryGetValue(cel.Path, out var rawExtra) && rawExtra != null)
        {
          if (TryConvertToDecimal(rawExtra, out var extraDec))
          {
            if (extraDec != 0m)
            {
              formattedVal = cel.Model is FormPercentModel
                ? $"{extraDec.ToString("0.##", CultureInfo.InvariantCulture)}%"
                : extraDec.ToString("0.##", CultureInfo.InvariantCulture);
            }
          }
          else if (rawExtra is bool extraBool)
          {
            if (extraBool)
            {
              formattedVal = L["Yes"];
            }
          }
          else
          {
            var s = rawExtra.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
              formattedVal = s;
            }
          }
        }

        if (!string.IsNullOrWhiteSpace(formattedVal))
        {
          extraLineFields ??= [];
          extraLineFields.Add(
            new AgentProposalFieldDto
            {
              Label = cel.Label ?? cel.Path,
              Value = formattedVal
            }
          );
        }
      }

      proposalLines.Add(
        new AgentProposalLineDto
        {
          Name = name,
          Qty = lineQty,
          Price = linePrice,
          Amount = lineAmount,
          Category = lineCategoryLabel,
          Fields = extraLineFields
        }
      );

      rawLineDicts.Add(lineDict);
      var addedLineIdx = rawLineDicts.Count - 1;
      foreach (var lpr in linePendingRefs)
      {
        lpr.LineIndex = addedLineIdx;
        newReferences.Add(lpr);
      }
    }

    // Declarative child-to-parent rollup strictly when declared on AddChild(...).SumTo(parentField, childField)
    if (rawLineDicts.Count > 0 && childBuilder.SumMappings.Count > 0)
    {
      foreach (var mapping in childBuilder.SumMappings)
      {
        if (string.IsNullOrWhiteSpace(mapping.ParentFieldPath) ||
            string.IsNullOrWhiteSpace(mapping.ChildFieldPath) ||
            explicitlyProvided.Contains(mapping.ParentFieldPath) ||
            formValues.TryGetValue(mapping.ParentFieldPath, out var curParentTotal) &&
            TryConvertToDecimal(curParentTotal, out var existingTotal) &&
            existingTotal != 0m)
        {
          continue;
        }

        var computedTotal = 0m;
        foreach (var dict in rawLineDicts)
        {
          if (dict.TryGetValue(mapping.ChildFieldPath, out var lineVal) &&
              TryConvertToDecimal(lineVal, out var decLineVal))
          {
            computedTotal += decLineVal;
          }
        }

        if (computedTotal != 0m)
        {
          formValues[mapping.ParentFieldPath] = computedTotal;
        }
      }
    }

    return (proposalLines, rawLineDicts);
  }
}
