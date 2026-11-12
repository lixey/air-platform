using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentProposalDisplayBuilder
{
  public static List<AgentProposalFieldDto> BuildDisplayFields(
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> originalFormValues,
    Dictionary<string, object?> formValues,
    IReadOnlyList<PendingNewReference> newReferences,
    HashSet<string> explicitlyProvided,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    bool isEdit,
    Localizer L)
  {
    var displayFields = new List<AgentProposalFieldDto>();

    foreach (var el in editableElements)
    {
      var label = el.Label ?? el.Path;
      var elNewRefs = newReferences
        .Where(nr => nr.LineIndex == null && string.Equals(nr.FieldPath, el.Path, StringComparison.OrdinalIgnoreCase))
        .ToList();

      if (isEdit)
      {
        var changed = HasFieldValueChanged(el, originalFormValues, formValues, elNewRefs);
        var oldDisplay = FormatFieldDisplayValue(el, originalFormValues, [], dictionaries, L, includeZeroMoney: changed);
        var newDisplay = FormatFieldDisplayValue(el, formValues, elNewRefs, dictionaries, L, includeZeroMoney: changed);

        if (changed)
        {
          var valText = !string.IsNullOrWhiteSpace(oldDisplay) && !string.IsNullOrWhiteSpace(newDisplay) &&
                        !string.Equals(oldDisplay, newDisplay, StringComparison.Ordinal)
            ? $"{oldDisplay} → {newDisplay}"
            : !string.IsNullOrWhiteSpace(newDisplay)
              ? newDisplay
              : !string.IsNullOrWhiteSpace(oldDisplay)
                ? $"{oldDisplay} → —"
                : null;

          if (!string.IsNullOrWhiteSpace(valText))
          {
            var (fieldBadge, fieldColor) = ResolveFieldRefBadge(elNewRefs, L, L["AgentChangedBadge"], UiColor.Amber);
            displayFields.Add(new AgentProposalFieldDto
            {
              Label = label,
              Value = valText,
              Badge = fieldBadge,
              Color = fieldColor
            });
          }
        }
        else
        {
          if (el.Type == UiFieldType.Checkbox &&
              formValues.TryGetValue(el.Path, out var bObj) &&
              bObj is true &&
              !explicitlyProvided.Contains(el.Path))
          {
            continue;
          }

          if (!string.IsNullOrWhiteSpace(newDisplay))
          {
            displayFields.Add(new AgentProposalFieldDto { Label = label, Value = newDisplay });
          }
        }

        continue;
      }

      if (el.Type == UiFieldType.Checkbox &&
          formValues.TryGetValue(el.Path, out var cObj) &&
          cObj is true &&
          !explicitlyProvided.Contains(el.Path))
      {
        continue;
      }

      var createDisplay = FormatFieldDisplayValue(el, formValues, elNewRefs, dictionaries, L, includeZeroMoney: false);
      if (!string.IsNullOrWhiteSpace(createDisplay))
      {
        var (createBadge, createColor) = ResolveFieldRefBadge(elNewRefs, L, null, null);
        displayFields.Add(new AgentProposalFieldDto
        {
          Label = label,
          Value = createDisplay,
          Badge = createBadge,
          Color = createColor
        });
      }
    }

    return displayFields;
  }

  public static (string? Badge, string? Color) ResolveFieldRefBadge(
    IReadOnlyList<PendingNewReference> elNewRefs,
    Localizer L,
    string? fallbackBadge,
    string? fallbackColor)
  {
    if (elNewRefs.Count == 0)
    {
      return (fallbackBadge, fallbackColor);
    }

    if (elNewRefs.FirstOrDefault(r => r.StepNumber is > 0) is { } stepRef)
    {
      return (
        L["AgentStepBadge", stepRef.StepNumber!.Value],
        stepRef.IsExistingRecordEdit ? UiColor.Blue : UiColor.Emerald);
    }

    return (L["AgentNewBadge"], UiColor.Emerald);
  }

  public static bool HasFieldValueChanged(
    FormElementDto el,
    Dictionary<string, object?> oldValues,
    Dictionary<string, object?> newValues,
    IReadOnlyList<PendingNewReference> elNewRefs)
  {
    if (elNewRefs.Count > 0)
    {
      return true;
    }

    oldValues.TryGetValue(el.Path, out var oldVal);
    newValues.TryGetValue(el.Path, out var newVal);

    if (el.Model is FormSelectModel select)
    {
      if (select.IsMultiple || el.ValueKind == FormValueKind.List)
      {
        var oldList = ExtractLongList(oldVal);
        var newList = ExtractLongList(newVal);
        return oldList.Count != newList.Count || !oldList.SequenceEqual(newList);
      }

      var oldId = TryConvertToLong(oldVal, out var oId) && oId > 0 ? oId : 0L;
      var newId = TryConvertToLong(newVal, out var nId) && nId > 0 ? nId : 0L;
      return oldId != newId;
    }

    if (el.Type == UiFieldType.Number || el.Model is FormMoneyModel)
    {
      var oldDec = TryConvertToDecimal(oldVal, out var oDec) ? oDec : 0m;
      var newDec = TryConvertToDecimal(newVal, out var nDec) ? nDec : 0m;
      return oldDec != newDec;
    }

    if (el.Type == UiFieldType.Checkbox)
    {
      return oldVal is true != newVal is true;
    }

    var oldStr = oldVal?.ToString()?.Trim() ?? string.Empty;
    var newStr = newVal?.ToString()?.Trim() ?? string.Empty;
    return !string.Equals(oldStr, newStr, StringComparison.Ordinal);
  }

  public static string? FormatFieldDisplayValue(
    FormElementDto el,
    Dictionary<string, object?> values,
    IReadOnlyList<PendingNewReference> elNewRefs,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Localizer L,
    bool includeZeroMoney = false)
  {
    if (el.Model is FormSelectModel select)
    {
      if (select.IsMultiple || el.ValueKind == FormValueKind.List)
      {
        var names = new List<string>();
        var ids = ExtractLongList(values.GetValueOrDefault(el.Path));
        if (ids.Count > 0)
        {
          names.AddRange(ResolveMultipleNames(ids, select, dictionaries));
        }

        foreach (var refName in elNewRefs.Select(r => r.Name))
        {
          if (!string.IsNullOrWhiteSpace(refName) && !names.Contains(refName, StringComparer.OrdinalIgnoreCase))
          {
            names.Add(refName);
          }
        }

        return names.Count > 0 ? string.Join(", ", names) : null;
      }

      if (elNewRefs.Count > 0)
      {
        return elNewRefs[0].Name;
      }

      if (values.TryGetValue(el.Path, out var singleVal) && TryConvertToLong(singleVal, out var refId) && refId > 0)
      {
        var refName = ResolveSingleName(refId, select, dictionaries);
        return !string.IsNullOrWhiteSpace(refName) ? refName : null;
      }

      return null;
    }

    if (!values.TryGetValue(el.Path, out var val) || val == null)
    {
      return null;
    }

    if (el.Model is FormMoneyModel moneyModel)
    {
      if (TryConvertToDecimal(val, out var decVal) && (decVal != 0m || includeZeroMoney))
      {
        return string.IsNullOrEmpty(moneyModel.Currency)
          ? decVal.ToString("N2", CultureInfo.CurrentUICulture)
          : $"{decVal.ToString("N2", CultureInfo.CurrentUICulture)} {moneyModel.Currency}";
      }

      return null;
    }

    if (el.Model is FormPercentModel)
    {
      if (TryConvertToDecimal(val, out var pctVal) && (pctVal != 0m || includeZeroMoney))
      {
        return $"{pctVal.ToString("0.##", CultureInfo.CurrentUICulture)}%";
      }

      return null;
    }

    if (el.Type == UiFieldType.Checkbox && val is bool bVal)
    {
      return bVal ? L["Yes"] : L["No"];
    }

    if (el.Type == UiFieldType.Number)
    {
      if (TryConvertToDecimal(val, out var numVal) && (numVal != 0m || includeZeroMoney))
      {
        return numVal.ToString("G29", CultureInfo.CurrentUICulture);
      }

      return null;
    }

    var str = val.ToString();
    return !string.IsNullOrWhiteSpace(str) ? str : null;
  }

  public static string BuildProposalTitle(
    GridCreateDescriptor desc,
    Dictionary<string, object?> formValues,
    IReadOnlyList<PendingNewReference> newReferences,
    IReadOnlyList<FormElementDto> elements)
  {
    var primaryTextPath = ResolvePrimaryTextFieldPath(elements);
    if (formValues.TryGetValue(primaryTextPath, out var nameVal) && nameVal is string nameStr && !string.IsNullOrWhiteSpace(nameStr))
    {
      return $"{desc.Label}: {nameStr}";
    }

    var moneyEl = elements.FirstOrDefault(e => e.Model is FormMoneyModel);
    if (moneyEl?.Model is FormMoneyModel moneyModel &&
        formValues.TryGetValue(moneyEl.Path, out var amtVal) &&
        TryConvertToDecimal(amtVal, out var decAmt) &&
        decAmt > 0)
    {
      var amtText = string.IsNullOrEmpty(moneyModel.Currency)
        ? decAmt.ToString("N2", CultureInfo.CurrentUICulture)
        : $"{decAmt.ToString("N2", CultureInfo.CurrentUICulture)} {moneyModel.Currency}";
      return $"{desc.Label} {amtText}";
    }

    if (newReferences.FirstOrDefault(nr => nr.LineIndex == null) is { } firstTopRef)
    {
      return $"{desc.Label}: {firstTopRef.Name}";
    }

    return desc.Label;
  }

  public static string? BuildProposalSubtitle(
    Dictionary<string, object?> formValues,
    IReadOnlyList<PendingNewReference> newReferences,
    IReadOnlyList<FormElementDto> elements,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries)
  {
    var primaryTextPath = ResolvePrimaryTextFieldPath(elements);
    var parts = new List<string>();
    foreach (var el in elements)
    {
      if (el.Model is FormMoneyModel || el.Path.Equals(primaryTextPath, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      var pending = newReferences.FirstOrDefault(nr =>
        nr.LineIndex == null && !nr.IsMultiple && string.Equals(nr.FieldPath, el.Path, StringComparison.OrdinalIgnoreCase));
      if (pending != null)
      {
        parts.Add(pending.Name);
      }
      else if (el.Model is FormSelectModel select &&
               !select.IsMultiple &&
               formValues.TryGetValue(el.Path, out var val) &&
               TryConvertToLong(val, out var id) &&
               id > 0)
      {
        var name = ResolveSingleName(id, select, dictionaries);
        if (!string.IsNullOrWhiteSpace(name))
        {
          parts.Add(name);
        }
      }

      if (parts.Count >= 2)
      {
        break;
      }
    }

    return parts.Count > 0 ? string.Join(" · ", parts) : null;
  }
}
