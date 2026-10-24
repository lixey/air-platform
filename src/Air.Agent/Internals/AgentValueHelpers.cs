namespace Platform.Agent.Internals;

internal static class AgentValueHelpers
{
  public static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
  {
    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
  };

  public static Dictionary<string, object?>? CleanGridRow(object? rawRow, bool stripZeroMoney = false)
  {
    if (rawRow is not IDictionary<string, object?> dict)
    {
      return null;
    }

    var clean = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    foreach (var (k, v) in dict)
    {
      if (v == null ||
          string.Equals(k, "ver", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(k, "unavailable", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(k, "idCover", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(k, "flags", StringComparison.OrdinalIgnoreCase) ||
          k.StartsWith('_') ||
          k.EndsWith("Color", StringComparison.Ordinal) && !string.Equals(k, "color", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (stripZeroMoney &&
          v is decimal decVal &&
          decVal == 0m &&
          !string.Equals(k, "amount", StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(k, "balance", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (v is Reference r)
      {
        if (r.Id > 0)
        {
          clean[k] = new Dictionary<string, object?> { ["id"] = r.Id, ["name"] = r.Name };
        }
      }
      else
      {
        clean[k] = v is IDictionary<string, object?> subDict &&
               subDict.TryGetValue("id", out var subId) &&
               TryConvertToLong(subId, out var sid) &&
               sid > 0 &&
               subDict.TryGetValue("name", out var subName)
          ? new Dictionary<string, object?> { ["id"] = sid, ["name"] = subName }
          : v;
      }
    }

    return clean;
  }

  public static object? UnwrapReferenceValue(object? val) => val switch
  {
    Reference r => r.Id > 0 ? r.Id : null,
    IEnumerable<Reference> list => list.Select(x => x.Id).ToList(),
    IDictionary<string, object?> dict when dict.TryGetValue("id", out var idObj) && TryConvertToLong(idObj, out var dId) => dId > 0 ? dId : null,
    JsonElement { ValueKind: JsonValueKind.Object } je when TryGetLong(je, "id") is > 0 and var jId => jId,
    _ => val
  };

  public static List<long> ExtractLongList(object? val)
  {
    var list = new List<long>();
    if (val is IEnumerable<long> longs)
    {
      list.AddRange(longs.Where(x => x > 0));
    }
    else if (val is JsonElement { ValueKind: JsonValueKind.Array } arr)
    {
      foreach (var item in arr.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var l) && l > 0)
        {
          list.Add(l);
        }
      }
    }

    return list;
  }

  public static Dictionary<string, object?> ExtractEqualityFilterMap(FilterNode? filter)
  {
    var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    CollectEqualityFilters(filter, map);
    return map;
  }

  private static void CollectEqualityFilters(FilterNode? node, Dictionary<string, object?> target)
  {
    switch (node)
    {
      case FilterRule { Operator: FilterOperator.Equals, Field: { Length: > 0 } field, Value: { } val }:
        target[field] = val;
        break;
      case FilterGroup { Logic: FilterLogic.And, Items: { } items }:
        foreach (var item in items)
        {
          CollectEqualityFilters(item, target);
        }

        break;
    }
  }

  public static IReadOnlyList<Dictionary<string, object?>> FilterRowsByReferenceFilter(
    IReadOnlyList<Dictionary<string, object?>> rows,
    FilterNode? filter)
  {
    if (filter == null || rows.Count == 0)
    {
      return rows;
    }

    var map = ExtractEqualityFilterMap(filter);
    if (map.Count == 0)
    {
      return rows;
    }

    var filtered = rows.Where(r => MatchesEqualityMap(r, map)).ToList();
    return filtered.Count > 0 ? filtered : rows;
  }

  public static bool MatchesEqualityMap(Dictionary<string, object?> row, Dictionary<string, object?> filterMap)
  {
    foreach (var (field, expectedVal) in filterMap)
    {
      if (expectedVal == null)
      {
        continue;
      }

      if (!row.TryGetValue(field, out var cellVal) || cellVal == null)
      {
        var altKey = field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && field.Length > 2
          ? field[..^2]
          : null;
        if (altKey == null || !row.TryGetValue(altKey, out cellVal) || cellVal == null)
        {
          continue;
        }
      }

      var unwrapped = UnwrapReferenceValue(cellVal);
      if (TryConvertToLong(expectedVal, out var expLong) && TryConvertToLong(unwrapped, out var actLong))
      {
        if (expLong != actLong)
        {
          return false;
        }
      }
      else if (!string.Equals(unwrapped?.ToString(), expectedVal.ToString(), StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }
    }

    return true;
  }

  public static Dictionary<string, object?>? FindDictionaryRowByName(
    IReadOnlyList<Dictionary<string, object?>> rows,
    string name,
    bool allowFuzzy = false)
  {
    var clean = name.Trim();
    var exact = rows.FirstOrDefault(r => string.Equals(GetRowDisplayName(r), clean, StringComparison.OrdinalIgnoreCase));
    if (exact != null || !allowFuzzy)
    {
      return exact;
    }

    return rows.FirstOrDefault(r =>
    {
      var rowName = GetRowDisplayName(r);
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

  public static string? GetRowDisplayName(Dictionary<string, object?>? row)
  {
    if (row == null)
    {
      return null;
    }

    foreach (var key in new[] { "name", "title", "code", "label" })
    {
      if (row.TryGetValue(key, out var val) && val != null)
      {
        return val.ToString();
      }
    }

    return null;
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
        return GetRowDisplayName(row);
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

  public static bool TryExtractSavedId(object? saveResult, out long id)
  {
    switch (saveResult)
    {
      case IEntity { Id: > 0 } entity:
        id = entity.Id;
        return true;
      case FormDataDto { Id: > 0 } dto:
        id = dto.Id.Value;
        return true;
      default:
        id = 0;
        return false;
    }
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

  public static Dictionary<string, object?> CloneFormValues(Dictionary<string, object?> source)
  {
    var clone = new Dictionary<string, object?>(source.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var (k, v) in source)
    {
      clone[k] = v is IEnumerable<long> list ? list.ToList() : v;
    }

    return clone;
  }

  public static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
  {
    if (element.ValueKind == JsonValueKind.Object)
    {
      if (element.TryGetProperty(name, out value))
      {
        return true;
      }

      foreach (var prop in element.EnumerateObject())
      {
        if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
        {
          value = prop.Value;
          return true;
        }
      }
    }

    value = default;
    return false;
  }

  public static string? TryGetString(JsonElement element, string name) =>
    TryGetPropertyIgnoreCase(element, name, out var prop) && prop.ValueKind == JsonValueKind.String
      ? prop.GetString()
      : null;

  public static long? TryGetRecordId(JsonElement element) =>
    (TryGetLong(element, "id") ?? TryGetLong(element, "recordId")) is > 0 and var id ? id : null;

  public static long? TryGetLong(JsonElement element, string name)
  {
    if (!TryGetPropertyIgnoreCase(element, name, out var prop))
    {
      return null;
    }

    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v))
    {
      return v;
    }

    if (prop.ValueKind == JsonValueKind.String && prop.GetString()?.Trim().TrimStart('#') is { Length: > 0 } str &&
        long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
    {
      return parsed;
    }

    return null;
  }

  public static decimal? TryGetDecimal(JsonElement element, string name)
  {
    if (!TryGetPropertyIgnoreCase(element, name, out var prop))
    {
      return null;
    }

    if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var v))
    {
      return v;
    }

    if (prop.ValueKind == JsonValueKind.String && TryParseFlexibleDecimal(prop.GetString(), out var parsed))
    {
      return parsed;
    }

    return null;
  }

  public static bool TryParseFlexibleDecimal(string? raw, out decimal result)
  {
    result = 0m;
    if (string.IsNullOrWhiteSpace(raw))
    {
      return false;
    }

    var clean = raw.Trim().Replace(" ", "").Replace("\u00A0", "");
    if (clean.Contains(',') && !clean.Contains('.'))
    {
      clean = clean.Replace(',', '.');
    }

    return decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
  }

  public static object? ConvertJsonValue(JsonElement prop, string fieldType)
  {
    switch (prop.ValueKind)
    {
      case JsonValueKind.Null or JsonValueKind.Undefined:
        return null;
      case JsonValueKind.True:
        return true;
      case JsonValueKind.False:
        return false;
      case JsonValueKind.Number when prop.TryGetInt64(out var lng):
        return lng;
      case JsonValueKind.Number when fieldType == UiFieldType.Number && prop.TryGetDecimal(out var dec):
        return dec;
      case JsonValueKind.Number when prop.TryGetDouble(out var dbl):
        return dbl;
      case JsonValueKind.String:
        {
          var str = prop.GetString();
          if (fieldType == UiFieldType.Checkbox && bool.TryParse(str?.Trim(), out var bVal))
          {
            return bVal;
          }

          if (fieldType == UiFieldType.Number)
          {
            if (string.IsNullOrWhiteSpace(str))
            {
              return null;
            }

            if (long.TryParse(str.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sLng))
            {
              return sLng;
            }

            if (TryParseFlexibleDecimal(str, out var sDec))
            {
              return sDec;
            }
          }

          return str;
        }
      default:
        return prop.Clone();
    }
  }

  public static bool TryConvertToLong(object? val, out long result)
  {
    switch (val)
    {
      case long l:
        result = l;
        return true;
      case int i:
        result = i;
        return true;
      case decimal dec when dec == Math.Truncate(dec):
        result = (long)dec;
        return true;
      case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetInt64(out var jl):
        result = jl;
        return true;
      case string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sl):
        result = sl;
        return true;
      default:
        result = 0;
        return false;
    }
  }

  public static bool TryConvertToDecimal(object? val, out decimal result)
  {
    switch (val)
    {
      case decimal d:
        result = d;
        return true;
      case double dbl:
        result = (decimal)dbl;
        return true;
      case float flt:
        result = (decimal)flt;
        return true;
      case long l:
        result = l;
        return true;
      case int i:
        result = i;
        return true;
      case JsonElement { ValueKind: JsonValueKind.Number } je when je.TryGetDecimal(out var jd):
        result = jd;
        return true;
      case string s when TryParseFlexibleDecimal(s, out var sd):
        result = sd;
        return true;
      default:
        result = 0m;
        return false;
    }
  }
}
