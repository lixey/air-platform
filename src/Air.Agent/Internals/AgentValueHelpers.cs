namespace Platform.Agent.Internals;

internal static class AgentValueHelpers
{
  public static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
  {
    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
  };

  public static Dictionary<string, object?>? CleanGridRow(object? rawRow, bool stripZeroDecimals = false)
  {
    IDictionary<string, object?> dict;
    long? rowDtoId = null;
    string? rowDtoForm = null;
    if (rawRow is RowDto rowDto)
    {
      dict = rowDto.Cells;
      rowDtoId = rowDto.Id;
      rowDtoForm = rowDto.Open?.Service;
    }
    else if (rawRow is IDictionary<string, object?> rawDict)
    {
      dict = rawDict;
    }
    else
    {
      return null;
    }

    var clean = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (rowDtoId.HasValue)
    {
      clean["id"] = rowDtoId.Value;
    }

    if (!string.IsNullOrWhiteSpace(rowDtoForm))
    {
      clean["form"] = rowDtoForm;
    }

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

      if (stripZeroDecimals && v is decimal decVal && decVal == 0m)
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
      case JsonValueKind.Number when fieldType == UiFieldType.Number && prop.TryGetDecimal(out var dec):
        return dec;
      case JsonValueKind.Number when fieldType == UiFieldType.Number && prop.TryGetDouble(out var numDbl):
        return numDbl;
      case JsonValueKind.Number when prop.TryGetInt64(out var lng):
        return lng;
      case JsonValueKind.Number when prop.TryGetDecimal(out var anyDec):
        return anyDec;
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

            if (TryParseFlexibleDecimal(str, out var sDec))
            {
              return sDec;
            }

            if (long.TryParse(str.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sLng))
            {
              return sLng;
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

  public static bool TryConvertToDecimal(object? val, out decimal result) =>
    Platform.App.Internals.FilterNodeEvaluator.TryConvertToDecimal(val, out result) ||
    val is string s && TryParseFlexibleDecimal(s, out result);
}
