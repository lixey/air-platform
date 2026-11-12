using Platform.App.Internals;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentRowValueHelpers
{
  public static Dictionary<string, object?> ExtractEqualityFilterMap(FilterNode? filter) =>
    FilterNodeEvaluator.ExtractEqualityValues(filter);

  public static IReadOnlyList<Dictionary<string, object?>> FilterRowsByReferenceFilter(
    IReadOnlyList<Dictionary<string, object?>> rows,
    FilterNode? filter)
  {
    if (filter == null || rows.Count == 0 || !RowsExposeFilterFields(rows, filter))
    {
      return rows;
    }

    return rows.Where(r => MatchesReferenceFilter(r, filter)).ToList();
  }

  private static bool RowsExposeFilterFields(IReadOnlyList<Dictionary<string, object?>> rows, FilterNode node) =>
    node switch
    {
      FilterGroup group => group.Items.Count == 0 || group.Items.All(item => RowsExposeFilterFields(rows, item)),
      FilterRule rule when !string.IsNullOrEmpty(rule.Field) =>
        string.Equals(rule.Operator, FilterOperator.IsNull, StringComparison.OrdinalIgnoreCase) ||
        rows.Any(r =>
          r.ContainsKey(rule.Field) ||
          rule.Field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
          rule.Field.Length > 2 &&
          r.ContainsKey(rule.Field[..^2])),
      _ => false
    };

  public static bool MatchesReferenceFilter(Dictionary<string, object?> row, FilterNode filter) =>
    FilterNodeEvaluator.Evaluate(filter, field =>
    {
      if (row.TryGetValue(field, out var cellVal) && cellVal != null)
      {
        return UnwrapReferenceValue(cellVal);
      }

      if (field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
          field.Length > 2 &&
          row.TryGetValue(field[..^2], out cellVal) &&
          cellVal != null)
      {
        return UnwrapReferenceValue(cellVal);
      }

      return null;
    });

  public static Dictionary<string, object?>? FindDictionaryRowByName(
    IReadOnlyList<Dictionary<string, object?>> rows,
    string name,
    bool allowFuzzy = false,
    IReadOnlyList<string>? labelPaths = null)
  {
    var clean = name.Trim();
    var exact = rows.FirstOrDefault(r =>
      string.Equals(GetRowDisplayName(r, labelPaths), clean, StringComparison.OrdinalIgnoreCase) ||
      labelPaths is { Count: > 0 } && labelPaths.Any(lp => r.TryGetValue(lp, out var lv) && string.Equals(lv?.ToString()?.Trim(), clean, StringComparison.OrdinalIgnoreCase)) ||
      string.Equals(GetRowDisplayName(r), clean, StringComparison.OrdinalIgnoreCase));
    if (exact != null || !allowFuzzy)
    {
      return exact;
    }

    return rows.FirstOrDefault(r =>
    {
      var rowName = GetRowDisplayName(r, labelPaths) ?? GetRowDisplayName(r);
      return !string.IsNullOrEmpty(rowName) &&
             (rowName.Contains(clean, StringComparison.OrdinalIgnoreCase) ||
              clean.Contains(rowName, StringComparison.OrdinalIgnoreCase));
    });
  }

  public static bool TryGetRowId(Dictionary<string, object?> row, out long id)
  {
    if (row.TryGetValue("id", out var idObj) && TryConvertToLong(idObj, out id))
    {
      return true;
    }

    id = 0;
    return false;
  }

  public static string? GetRowDisplayName(Dictionary<string, object?>? row, IReadOnlyList<string>? labelPaths = null)
  {
    if (row == null)
    {
      return null;
    }

    if (labelPaths is { Count: > 0 })
    {
      var parts = new List<string>();
      foreach (var path in labelPaths)
      {
        if (row.TryGetValue(path, out var pathVal) && pathVal != null)
        {
          var s = pathVal is IDictionary<string, object?> subDict && subDict.TryGetValue("name", out var subName)
            ? subName?.ToString()?.Trim()
            : pathVal.ToString()?.Trim();
          if (!string.IsNullOrWhiteSpace(s))
          {
            parts.Add(s);
          }
        }
      }

      if (parts.Count > 0)
      {
        return string.Join(" ", parts);
      }
    }

    foreach (var key in new[] { "name", "title", "code", "label", "fullName", "number" })
    {
      if (row.TryGetValue(key, out var val) && val != null && val.ToString()?.Trim() is { Length: > 0 } str)
      {
        return str;
      }
    }

    foreach (var (k, v) in row)
    {
      if (v is string s &&
          !string.IsNullOrWhiteSpace(s) &&
          !string.Equals(k, "id", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(k, "form", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(k, "color", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(k, "kind", StringComparison.OrdinalIgnoreCase))
      {
        return s.Trim();
      }
    }

    return null;
  }

  public static string ResolvePrimaryTextFieldPath(
    IReadOnlyList<FormElementDto> elements,
    IReadOnlyList<string>? referenceLabelPaths = null)
  {
    var editableText = elements
      .Where(e => AgentFeatureRegistry.IsEditableElement(e) &&
                  e.Type == UiFieldType.Text &&
                  e.Model is not FormSelectModel)
      .ToList();

    if (referenceLabelPaths is { Count: > 0 })
    {
      foreach (var labelPath in referenceLabelPaths)
      {
        var matched = editableText.FirstOrDefault(e => string.Equals(e.Path, labelPath, StringComparison.OrdinalIgnoreCase));
        if (matched != null)
        {
          return matched.Path;
        }
      }
    }

    foreach (var preferred in new[] { "name", "title", "code", "label", "fullName", "number" })
    {
      var matched = editableText.FirstOrDefault(e => string.Equals(e.Path, preferred, StringComparison.OrdinalIgnoreCase));
      if (matched != null)
      {
        return matched.Path;
      }
    }

    var firstRequired = editableText.FirstOrDefault(e => e.IsRequired);
    if (firstRequired != null)
    {
      return firstRequired.Path;
    }

    return editableText.FirstOrDefault()?.Path ?? "name";
  }

  public static string? ResolveSingleName(
    long id,
    FormSelectModel select,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries)
  {
    if (select.Options is { Count: > 0 })
    {
      var opt = select.Options.FirstOrDefault(o => o.Id == id);
      if (opt != null)
      {
        return opt.Name;
      }
    }

    if (!string.IsNullOrEmpty(select.ReferenceService) &&
        dictionaries.TryGetValue(select.ReferenceService, out var rows))
    {
      var row = rows.FirstOrDefault(r => TryGetRowId(r, out var rid) && rid == id);
      if (row != null)
      {
        return GetRowDisplayName(row, select.ReferenceLabelPaths);
      }
    }

    return $"#{id}";
  }

  public static List<string> ResolveMultipleNames(
    IReadOnlyList<long> ids,
    FormSelectModel select,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries)
  {
    var names = new List<string>();
    foreach (var id in ids)
    {
      var name = ResolveSingleName(id, select, dictionaries);
      if (!string.IsNullOrWhiteSpace(name))
      {
        names.Add(name);
      }
    }

    return names;
  }

  public static string FormatRefusedException(
    FormRefusedException refusedEx,
    IReadOnlyList<FormElementDto> elements,
    string? contextPrefix = null)
  {
    var firstMsg = refusedEx.Messages.FirstOrDefault();
    var detail = firstMsg?.Detail ?? refusedEx.Message;
    if (!string.IsNullOrEmpty(firstMsg?.Path))
    {
      var fieldLabel = elements.FirstOrDefault(e => string.Equals(e.Path, firstMsg.Path, StringComparison.OrdinalIgnoreCase))?.Label
        ?? firstMsg.Path;
      detail = $"{fieldLabel}: {detail}";
    }

    return string.IsNullOrWhiteSpace(contextPrefix) ? detail : $"{contextPrefix} — {detail}";
  }
}
