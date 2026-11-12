using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalChildRowParser
{
  public static async Task<(AgentProposalChildRowDto RowDto, Dictionary<string, object?> RowDict, List<PendingNewReference> PendingRefs)?> ParseSingleChildRowAsync(
    JsonElement item,
    bool isEdit,
    long? recordId,
    string parentServicePath,
    IFormService childForm,
    Dictionary<string, object?> childDefaults,
    IReadOnlyList<FormElementDto> childEditableElements,
    Dictionary<string, object?> parentFormValues,
    IReadOnlyList<PendingNewReference> parentNewReferences,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Func<string, string?, AgentProposalDto?> findReferencedProposal,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    var itemRowId = isEdit ? TryGetRecordId(item) : null;
    var baseValues = itemRowId is > 0
      ? (await childForm.PrepareDraftAsync(recordId: itemRowId.Value, parentId: recordId, parentService: parentServicePath, ct: ct)).Values
      : childDefaults;

    var rowDict = new Dictionary<string, object?>(baseValues, StringComparer.OrdinalIgnoreCase);
    if (itemRowId is > 0)
    {
      rowDict["id"] = itemRowId.Value;
    }

    var rowRefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var explicitlySetFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var rowPendingRefs = new List<PendingNewReference>();

    foreach (var cel in childEditableElements)
    {
      var hasCellProp = TryGetPropertyIgnoreCase(item, cel.Path, out var rawCellProp);
      if (cel.Model is FormSelectModel cSelect && !string.IsNullOrEmpty(cSelect.ReferenceService))
      {
        await ResolveChildSelectCellAsync(
          item,
          cel,
          cSelect,
          hasCellProp,
          rawCellProp,
          childEditableElements,
          rowDict,
          rowRefLabels,
          explicitlySetFields,
          rowPendingRefs,
          parentFormValues,
          parentNewReferences,
          dictionaries,
          findReferencedProposal,
          db,
          app,
          L,
          ct);
        continue;
      }

      if (hasCellProp && rawCellProp.ValueKind != JsonValueKind.Undefined)
      {
        explicitlySetFields.Add(cel.Path);
        rowDict[cel.Path] = ConvertJsonValue(rawCellProp, cel.Type);
      }
    }

    if (explicitlySetFields.Count == 0 && itemRowId is not > 0)
    {
      return null;
    }

    var pendingPaths = rowPendingRefs.Where(r => !r.IsExistingRecordEdit).Select(r => r.FieldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var overrides = explicitlySetFields
      .Where(k => !pendingPaths.Contains(k))
      .ToDictionary(k => k, k => rowDict.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);
    var draft = await childForm.PrepareDraftAsync(
      recordId: itemRowId,
      overrides: overrides,
      pendingReferencePaths: pendingPaths,
      parentId: isEdit ? recordId : null,
      parentService: parentServicePath,
      ct: ct);

    foreach (var (k, v) in draft.Values)
    {
      if (!pendingPaths.Contains(k) && v != null &&
          (!explicitlySetFields.Contains(k) ||
           !rowDict.TryGetValue(k, out var curV) ||
           curV == null ||
           TryConvertToDecimal(curV, out var curDec) && curDec == 0m))
      {
        rowDict[k] = v;
      }
    }

    var cells = FormatChildRowCells(childEditableElements, rowDict, rowRefLabels, explicitlySetFields, L);
    if (cells.Count == 0 && itemRowId is not > 0)
    {
      return null;
    }

    return (new AgentProposalChildRowDto { Id = itemRowId, Cells = cells }, rowDict, rowPendingRefs);
  }

  private static async Task ResolveChildSelectCellAsync(
    JsonElement item,
    FormElementDto cel,
    FormSelectModel cSelect,
    bool hasCellProp,
    JsonElement rawCellProp,
    IReadOnlyList<FormElementDto> childEditableElements,
    Dictionary<string, object?> rowDict,
    Dictionary<string, string> rowRefLabels,
    HashSet<string> explicitlySetFields,
    List<PendingNewReference> rowPendingRefs,
    Dictionary<string, object?> parentFormValues,
    IReadOnlyList<PendingNewReference> parentNewReferences,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Func<string, string?, AgentProposalDto?> findReferencedProposal,
    PlatformDbContext db,
    PlatformAppService app,
    Localizer L,
    CancellationToken ct)
  {
    long? resolvedId = null;
    string? resolvedName = null;
    var handledByStepRef = false;

    if (!hasCellProp && cel.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && cel.Path.Length > 2)
    {
      var baseName = cel.Path[..^2];
      var allowNameFallback = !childEditableElements.Any(e => string.Equals(e.Path, "name", StringComparison.OrdinalIgnoreCase)) &&
                              ReferenceEquals(cel, childEditableElements.FirstOrDefault(e => e.Model is FormSelectModel));
      var aliases = allowNameFallback ? new[] { $"{baseName}Name", baseName, "name" } : new[] { $"{baseName}Name", baseName };
      foreach (var alias in aliases)
      {
        if (TryGetPropertyIgnoreCase(item, alias, out rawCellProp))
        {
          hasCellProp = true;
          break;
        }
      }
    }

    if (hasCellProp)
    {
      explicitlySetFields.Add(cel.Path);
      if (AgentStepGraphHelper.TryExtractRefKey(rawCellProp, out var refKey))
      {
        handledByStepRef = true;
        var target = findReferencedProposal(refKey, cSelect.ReferenceService)
          ?? throw new InvalidOperationException(L["MutationRefused"]);
        var targetLabel = AgentStepGraphHelper.GetProposalShortLabel(target);
        if (target.RecordId is > 0)
        {
          resolvedId = target.RecordId.Value;
          resolvedName = targetLabel;
        }
        else
        {
          rowRefLabels[cel.Path] = targetLabel;
        }

        rowPendingRefs.Add(new PendingNewReference
        {
          FieldPath = cel.Path,
          Name = targetLabel,
          FormService = target.Service,
          ProposalId = target.Id,
          StepKey = refKey,
          StepNumber = target.StepNumber,
          IsExistingRecordEdit = target.RecordId is > 0
        });
      }
      else
      {
        (resolvedId, resolvedName, _) = await AgentReferenceResolver.ResolveReferenceValueAsync(
          rawCellProp,
          cSelect.ReferenceService!,
          cSelect.ReferenceFilter,
          allowFuzzy: true,
          allowWrongKindFallback: false,
          dictionaries,
          db,
          app,
          ct,
          cSelect.ReferenceLabelPaths);
      }
    }

    if (!handledByStepRef && resolvedId == null &&
        parentFormValues.TryGetValue(cel.Path, out var parentVal) &&
        TryConvertToLong(parentVal, out var parentId) && parentId > 0)
    {
      resolvedId = parentId;
    }

    rowDict[cel.Path] = resolvedId;
    if (resolvedId is > 0)
    {
      var label = !string.IsNullOrWhiteSpace(resolvedName) ? resolvedName : ResolveSingleName(resolvedId.Value, cSelect, dictionaries);
      if (!string.IsNullOrWhiteSpace(label))
      {
        rowRefLabels[cel.Path] = label;
      }
    }
    else if (!rowRefLabels.ContainsKey(cel.Path) &&
             parentNewReferences.FirstOrDefault(nr => nr.LineIndex == null && string.Equals(nr.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)) is { } pRef)
    {
      rowRefLabels[cel.Path] = pRef.Name;
    }
  }

  private static Dictionary<string, string> FormatChildRowCells(
    IReadOnlyList<FormElementDto> childEditableElements,
    Dictionary<string, object?> rowDict,
    Dictionary<string, string> rowRefLabels,
    HashSet<string> explicitlySetFields,
    Localizer L)
  {
    var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var cel in childEditableElements)
    {
      if (string.Equals(cel.Path, "sortOrder", StringComparison.OrdinalIgnoreCase) && !explicitlySetFields.Contains(cel.Path))
      {
        continue;
      }

      if (rowRefLabels.TryGetValue(cel.Path, out var refLabel) && !string.IsNullOrWhiteSpace(refLabel))
      {
        cells[cel.Path] = refLabel;
        continue;
      }

      if (!rowDict.TryGetValue(cel.Path, out var rawVal) || rawVal == null)
      {
        continue;
      }

      if (cel.Model is FormMoneyModel moneyModel)
      {
        if (TryConvertToDecimal(rawVal, out var decMoney) && (decMoney != 0m || explicitlySetFields.Contains(cel.Path)))
        {
          cells[cel.Path] = string.IsNullOrEmpty(moneyModel.Currency)
            ? decMoney.ToString("N2", CultureInfo.CurrentUICulture)
            : $"{decMoney.ToString("N2", CultureInfo.CurrentUICulture)} {moneyModel.Currency}";
        }
      }
      else if (cel.Model is FormPercentModel)
      {
        if (TryConvertToDecimal(rawVal, out var decPct) && (decPct != 0m || explicitlySetFields.Contains(cel.Path)))
        {
          cells[cel.Path] = $"{decPct.ToString("0.##", CultureInfo.CurrentUICulture)}%";
        }
      }
      else if (cel.Type == UiFieldType.Number)
      {
        if (TryConvertToDecimal(rawVal, out var decNum) && (decNum != 0m || explicitlySetFields.Contains(cel.Path)))
        {
          cells[cel.Path] = decNum.ToString("G29", CultureInfo.CurrentUICulture);
        }
      }
      else if (cel.Type == UiFieldType.Checkbox && rawVal is bool b)
      {
        if (b || explicitlySetFields.Contains(cel.Path))
        {
          cells[cel.Path] = b ? L["Yes"] : L["No"];
        }
      }
      else if (cel.Model is not FormSelectModel)
      {
        var s = rawVal.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(s))
        {
          cells[cel.Path] = s;
        }
      }
    }

    return cells;
  }
}
