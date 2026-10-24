namespace Platform.Agent;

public static partial class AgentProposalBuilder
{
  private static List<AgentProposalFieldDto> BuildDisplayFields(
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> originalFormValues,
    Dictionary<string, object?> formValues,
    IReadOnlyList<PendingNewReference> newReferences,
    HashSet<string> explicitlyProvided,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    bool isEdit,
    Localizer L,
    out string? currencyLabel)
  {
    var displayFields = new List<AgentProposalFieldDto>();
    currencyLabel = ResolveCurrencyLabel(formValues, editableElements, dictionaries);
    var oldCurrencyLabel = isEdit
      ? (ResolveCurrencyLabel(originalFormValues, editableElements, dictionaries) ?? currencyLabel)
      : currencyLabel;

    foreach (var el in editableElements)
    {
      var label = el.Label ?? el.Path;
      var elNewRefs = newReferences
        .Where(nr => string.Equals(nr.FieldPath, el.Path, StringComparison.OrdinalIgnoreCase))
        .ToList();

      if (isEdit)
      {
        var changed = HasFieldValueChanged(el, originalFormValues, formValues, elNewRefs);
        var oldDisplay = FormatFieldDisplayValue(el, originalFormValues, [], oldCurrencyLabel, dictionaries, L, includeZeroMoney: changed);
        var newDisplay = FormatFieldDisplayValue(el, formValues, elNewRefs, currencyLabel, dictionaries, L, includeZeroMoney: changed);

        if (changed)
        {
          string? valText = null;
          if (!string.IsNullOrWhiteSpace(oldDisplay) && !string.IsNullOrWhiteSpace(newDisplay) &&
              !string.Equals(oldDisplay, newDisplay, StringComparison.Ordinal))
          {
            valText = $"{oldDisplay} → {newDisplay}";
          }
          else if (!string.IsNullOrWhiteSpace(newDisplay))
          {
            valText = newDisplay;
          }
          else if (!string.IsNullOrWhiteSpace(oldDisplay))
          {
            valText = $"{oldDisplay} → —";
          }

          if (!string.IsNullOrWhiteSpace(valText))
          {
            displayFields.Add(new AgentProposalFieldDto
            {
              Label = label,
              Value = valText,
              Badge = elNewRefs.Count > 0 ? L["AgentNewBadge"] : L["AgentChangedBadge"],
              Color = elNewRefs.Count > 0 ? UiColor.Emerald : UiColor.Amber
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
            displayFields.Add(new AgentProposalFieldDto
            {
              Label = label,
              Value = newDisplay
            });
          }
        }

        continue;
      }

      if (el.Model is FormSelectModel select)
      {
        if (select.IsMultiple || el.ValueKind == FormValueKind.List)
        {
          var names = new List<string>();
          if (formValues.TryGetValue(el.Path, out var listVal) && listVal is IEnumerable<long> idList)
          {
            var ids = idList.ToList();
            if (ids.Count > 0)
            {
              names.AddRange(ResolveMultipleNames(ids, select, dictionaries));
            }
          }

          names.AddRange(elNewRefs.Select(r => r.Name));
          if (names.Count > 0)
          {
            displayFields.Add(new AgentProposalFieldDto
            {
              Label = label,
              Value = string.Join(", ", names),
              Badge = elNewRefs.Count > 0 ? L["AgentNewBadge"] : null,
              Color = elNewRefs.Count > 0 ? UiColor.Emerald : null
            });
          }

          continue;
        }

        if (elNewRefs.Count > 0)
        {
          displayFields.Add(new AgentProposalFieldDto
          {
            Label = label,
            Value = elNewRefs[0].Name,
            Badge = L["AgentNewBadge"],
            Color = UiColor.Emerald
          });
          continue;
        }

        if (formValues.TryGetValue(el.Path, out var singleVal) && TryConvertToLong(singleVal, out var refId) && refId > 0)
        {
          var refName = ResolveSingleName(refId, select, dictionaries);
          if (!string.IsNullOrWhiteSpace(refName))
          {
            displayFields.Add(new AgentProposalFieldDto { Label = label, Value = refName });
          }
        }

        continue;
      }

      if (!formValues.TryGetValue(el.Path, out var val) || val == null)
      {
        continue;
      }

      if (el.Model is FormMoneyModel)
      {
        if (TryConvertToDecimal(val, out var decVal) && decVal != 0m)
        {
          var formatted = string.IsNullOrEmpty(currencyLabel)
            ? decVal.ToString("N2", CultureInfo.CurrentUICulture)
            : $"{decVal.ToString("N2", CultureInfo.CurrentUICulture)} {currencyLabel}";
          displayFields.Add(new AgentProposalFieldDto { Label = label, Value = formatted });
        }

        continue;
      }

      if (el.Type == UiFieldType.Checkbox && val is bool bVal)
      {
        if (explicitlyProvided.Contains(el.Path) || !bVal)
        {
          displayFields.Add(new AgentProposalFieldDto { Label = label, Value = bVal ? L["Yes"] : L["No"] });
        }

        continue;
      }

      if (el.Type == UiFieldType.Number)
      {
        if (TryConvertToDecimal(val, out var numVal) && numVal != 0m)
        {
          displayFields.Add(new AgentProposalFieldDto { Label = label, Value = numVal.ToString("G29", CultureInfo.CurrentUICulture) });
        }

        continue;
      }

      var str = val.ToString();
      if (!string.IsNullOrWhiteSpace(str))
      {
        displayFields.Add(new AgentProposalFieldDto { Label = label, Value = str });
      }
    }

    return displayFields;
  }

  private static bool HasFieldValueChanged(
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
      var oldBool = oldVal is true;
      var newBool = newVal is true;
      return oldBool != newBool;
    }

    var oldStr = oldVal?.ToString()?.Trim() ?? string.Empty;
    var newStr = newVal?.ToString()?.Trim() ?? string.Empty;
    return !string.Equals(oldStr, newStr, StringComparison.Ordinal);
  }

  private static string? FormatFieldDisplayValue(
    FormElementDto el,
    Dictionary<string, object?> values,
    IReadOnlyList<PendingNewReference> elNewRefs,
    string? currencyLabel,
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

        names.AddRange(elNewRefs.Select(r => r.Name));
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

    if (el.Model is FormMoneyModel)
    {
      if (TryConvertToDecimal(val, out var decVal) && (decVal != 0m || includeZeroMoney))
      {
        return string.IsNullOrEmpty(currencyLabel)
          ? decVal.ToString("N2", CultureInfo.CurrentUICulture)
          : $"{decVal.ToString("N2", CultureInfo.CurrentUICulture)} {currencyLabel}";
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

  private static string? ResolveCurrencyLabel(
    Dictionary<string, object?> formValues,
    IReadOnlyList<FormElementDto> elements,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries)
  {
    foreach (var el in elements)
    {
      if (el.Model is not FormSelectModel { ReferenceService: { Length: > 0 } refSvc } ||
          !formValues.TryGetValue(el.Path, out var val) ||
          !TryConvertToLong(val, out var id) ||
          id <= 0 ||
          !dictionaries.TryGetValue(refSvc, out var rows))
      {
        continue;
      }

      var row = rows.FirstOrDefault(r => TryGetRowId(r, out var rid) && rid == id);
      if (row == null)
      {
        continue;
      }

      if (row.TryGetValue("name", out var accNameObj) && accNameObj is string accName)
      {
        var openIdx = accName.LastIndexOf('(');
        var closeIdx = accName.LastIndexOf(')');
        if (openIdx >= 0 && closeIdx > openIdx + 1)
        {
          var token = accName[(openIdx + 1)..closeIdx].Trim();
          if (token.Length is >= 2 and <= 5 && token.All(char.IsLetter))
          {
            return token.ToUpperInvariant();
          }
        }
      }

      if (row.TryGetValue("currency", out var currObj))
      {
        if (currObj is IDictionary<string, object?> currDict)
        {
          if (currDict.TryGetValue("code", out var cCode) && cCode?.ToString() is { Length: > 0 } codeStr)
          {
            return codeStr;
          }

          if (currDict.TryGetValue("name", out var cName))
          {
            return cName?.ToString();
          }
        }

        if (currObj is string cStr)
        {
          return cStr;
        }
      }
    }

    return null;
  }

  private static string BuildProposalTitle(
    GridCreateDescriptor desc,
    Dictionary<string, object?> formValues,
    IReadOnlyList<PendingNewReference> newReferences,
    IReadOnlyList<FormElementDto> elements,
    string? currencyLabel)
  {
    if (formValues.TryGetValue("name", out var nameVal) && nameVal is string nameStr && !string.IsNullOrWhiteSpace(nameStr))
    {
      return $"{desc.Label}: {nameStr}";
    }

    var moneyEl = elements.FirstOrDefault(e => e.Model is FormMoneyModel);
    if (moneyEl != null && formValues.TryGetValue(moneyEl.Path, out var amtVal) && TryConvertToDecimal(amtVal, out var decAmt) && decAmt > 0)
    {
      var amtText = string.IsNullOrEmpty(currencyLabel)
        ? decAmt.ToString("N2", CultureInfo.CurrentUICulture)
        : $"{decAmt.ToString("N2", CultureInfo.CurrentUICulture)} {currencyLabel}";
      return $"{desc.Label} {amtText}";
    }

    if (newReferences.Count > 0)
    {
      return $"{desc.Label}: {newReferences[0].Name}";
    }

    return desc.Label;
  }

  private static string? BuildProposalSubtitle(
    Dictionary<string, object?> formValues,
    IReadOnlyList<PendingNewReference> newReferences,
    IReadOnlyList<FormElementDto> elements,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries)
  {
    var parts = new List<string>();
    foreach (var el in elements)
    {
      if (el.Model is FormMoneyModel || el.Path.Equals("name", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (el.Model is FormSelectModel { ReferenceService: { Length: > 0 } refSvc } &&
          dictionaries.TryGetValue(refSvc, out var refRows) &&
          refRows.Any(r => r.ContainsKey("currency")))
      {
        continue;
      }

      var pending = newReferences.FirstOrDefault(nr => !nr.IsMultiple && string.Equals(nr.FieldPath, el.Path, StringComparison.OrdinalIgnoreCase));
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
