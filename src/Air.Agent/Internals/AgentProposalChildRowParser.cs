using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalChildRowParser
{
  public static async Task<(AgentProposalChildRowDto RowDto, Dictionary<string, object?> RowDict, List<PendingNewReference> PendingRefs)?> ParseSingleChildRowAsync(
    ChildRowParseScope scope,
    JsonElement item,
    CancellationToken ct)
  {
    var ctx = scope.Context;
    var parent = scope.ParentDraft;
    var itemRowId = parent.IsEdit ? TryGetRecordId(item) : null;
    Dictionary<string, object?> baseValues;
    if (itemRowId is > 0)
    {
      baseValues = (await scope.ChildForm.PrepareDraftAsync(
        recordId: itemRowId.Value,
        parentId: parent.RecordId,
        parentService: parent.Target.ServicePath,
        ct: ct)).Values;
      if (scope.ChildForm.IsFormReadOnly)
      {
        throw new InvalidOperationException(ctx.Localizer["FormReadOnly"]);
      }
    }
    else
    {
      baseValues = scope.ChildDefaults;
    }

    var row = new ChildRowBuffer(baseValues, itemRowId);

    foreach (var cel in scope.EditableElements)
    {
      if (cel.Model is FormSelectModel cSelect && !string.IsNullOrEmpty(cSelect.ReferenceService))
      {
        await ResolveChildSelectCellAsync(scope, item, cel, cSelect, row, ct);
        continue;
      }

      if (TryGetPropertyIgnoreCase(item, cel.Path, out var rawCellProp) && rawCellProp.ValueKind != JsonValueKind.Undefined)
      {
        row.ExplicitFields.Add(cel.Path);
        row.Values[cel.Path] = ConvertJsonValue(rawCellProp, cel.Type);
      }
    }

    if (row.ExplicitFields.Count == 0 && itemRowId is not > 0)
    {
      return null;
    }

    var pendingPaths = row.PendingRefs.Where(r => !r.IsExistingRecordEdit).Select(r => r.FieldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var overrides = row.ExplicitFields
      .Where(k => !pendingPaths.Contains(k))
      .ToDictionary(k => k, k => row.Values.GetValueOrDefault(k), StringComparer.OrdinalIgnoreCase);
    var draft = await scope.ChildForm.PrepareDraftAsync(
      recordId: itemRowId,
      overrides: overrides,
      pendingReferencePaths: pendingPaths,
      parentId: parent.IsEdit ? parent.RecordId : null,
      parentService: parent.Target.ServicePath,
      ct: ct);

    foreach (var (k, v) in draft.Values)
    {
      if (!pendingPaths.Contains(k) && v != null &&
          (!row.ExplicitFields.Contains(k) ||
           !row.Values.TryGetValue(k, out var curV) ||
           curV == null ||
           TryConvertToDecimal(curV, out var curDec) && curDec == 0m))
      {
        row.Values[k] = v;
      }
    }

    var cells = FormatChildRowCells(scope.EditableElements, row, ctx.Localizer);
    if (cells.Count == 0 && itemRowId is not > 0)
    {
      return null;
    }

    return (new AgentProposalChildRowDto { Id = itemRowId, Cells = cells }, row.Values, row.PendingRefs);
  }

  private static async Task ResolveChildSelectCellAsync(
    ChildRowParseScope scope,
    JsonElement item,
    FormElementDto cel,
    FormSelectModel cSelect,
    ChildRowBuffer row,
    CancellationToken ct)
  {
    var ctx = scope.Context;
    var parent = scope.ParentDraft;
    var hasCellProp = TryGetPropertyIgnoreCase(item, cel.Path, out var rawCellProp);
    long? resolvedId = null;
    string? resolvedName = null;
    var handledByStepRef = false;

    if (!hasCellProp && cel.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && cel.Path.Length > 2)
    {
      var baseName = cel.Path[..^2];
      var allowNameFallback = !scope.EditableElements.Any(e => string.Equals(e.Path, "name", StringComparison.OrdinalIgnoreCase)) &&
                              ReferenceEquals(cel, scope.EditableElements.FirstOrDefault(e => e.Model is FormSelectModel));
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
      row.ExplicitFields.Add(cel.Path);
      if (AgentStepGraphHelper.TryExtractRefKey(rawCellProp, out var refKey))
      {
        handledByStepRef = true;
        var target = ctx.FindReferencedProposal(refKey, cSelect.ReferenceService)
          ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
        var targetLabel = AgentStepGraphHelper.GetProposalShortLabel(target);
        if (target.RecordId is > 0)
        {
          resolvedId = target.RecordId.Value;
          resolvedName = targetLabel;
        }
        else
        {
          row.RefLabels[cel.Path] = targetLabel;
        }

        row.PendingRefs.Add(new PendingNewReference
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
          parent.Dictionaries,
          ctx.Db,
          ctx.App,
          ct,
          cSelect.ReferenceLabelPaths);
      }
    }

    if (!handledByStepRef && resolvedId == null &&
        parent.FormValues.TryGetValue(cel.Path, out var parentVal) &&
        TryConvertToLong(parentVal, out var parentId) && parentId > 0)
    {
      resolvedId = parentId;
    }

    row.Values[cel.Path] = resolvedId;
    if (resolvedId is > 0)
    {
      var label = !string.IsNullOrWhiteSpace(resolvedName) ? resolvedName : ResolveSingleName(resolvedId.Value, cSelect, parent.Dictionaries);
      if (!string.IsNullOrWhiteSpace(label))
      {
        row.RefLabels[cel.Path] = label;
      }
    }
    else if (!row.RefLabels.ContainsKey(cel.Path) &&
             parent.NewReferences.FirstOrDefault(nr => nr.LineIndex == null && string.Equals(nr.FieldPath, cel.Path, StringComparison.OrdinalIgnoreCase)) is { } pRef)
    {
      row.RefLabels[cel.Path] = pRef.Name;
    }
  }

  private static Dictionary<string, string> FormatChildRowCells(
    IReadOnlyList<FormElementDto> childEditableElements,
    ChildRowBuffer row,
    Localizer L)
  {
    var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var cel in childEditableElements)
    {
      if (string.Equals(cel.Path, "sortOrder", StringComparison.OrdinalIgnoreCase) && !row.ExplicitFields.Contains(cel.Path))
      {
        continue;
      }

      if (row.RefLabels.TryGetValue(cel.Path, out var refLabel) && !string.IsNullOrWhiteSpace(refLabel))
      {
        cells[cel.Path] = refLabel;
        continue;
      }

      if (!row.Values.TryGetValue(cel.Path, out var rawVal) || rawVal == null)
      {
        continue;
      }

      if (cel.Model is FormMoneyModel moneyModel)
      {
        if (TryConvertToDecimal(rawVal, out var decMoney) && (decMoney != 0m || row.ExplicitFields.Contains(cel.Path)))
        {
          cells[cel.Path] = string.IsNullOrEmpty(moneyModel.Currency)
            ? decMoney.ToString("N2", CultureInfo.CurrentUICulture)
            : $"{decMoney.ToString("N2", CultureInfo.CurrentUICulture)} {moneyModel.Currency}";
        }
      }
      else if (cel.Model is FormPercentModel)
      {
        if (TryConvertToDecimal(rawVal, out var decPct) && (decPct != 0m || row.ExplicitFields.Contains(cel.Path)))
        {
          cells[cel.Path] = $"{decPct.ToString("0.##", CultureInfo.CurrentUICulture)}%";
        }
      }
      else if (cel.Type == UiFieldType.Number)
      {
        if (TryConvertToDecimal(rawVal, out var decNum) && (decNum != 0m || row.ExplicitFields.Contains(cel.Path)))
        {
          cells[cel.Path] = decNum.ToString("G29", CultureInfo.CurrentUICulture);
        }
      }
      else if (cel.Type == UiFieldType.Checkbox && rawVal is bool b)
      {
        if (b || row.ExplicitFields.Contains(cel.Path))
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
