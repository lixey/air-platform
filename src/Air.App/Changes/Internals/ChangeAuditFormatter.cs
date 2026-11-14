using Platform.App.Screens.Internals;

namespace Platform.App.Changes.Internals;

internal static class ChangeAuditFormatter
{
  private static readonly JsonSerializerOptions UnescapedJsonOptions = new()
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  private static readonly string[] SummaryPropertyNames = ["Name", "Title", "Code", "Login", "Note"];

  public static Dictionary<string, JsonElement> ParseJsonDictionary(string? json)
  {
    var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
    if (string.IsNullOrWhiteSpace(json))
    {
      return dict;
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      if (doc.RootElement.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
          dict[prop.Name] = prop.Value.Clone();
        }
      }
    }
    catch
    {
    }

    return dict;
  }

  public static string ResolveFieldLabel(Feature? feat, Localizer modL, Type? entityType, string propName)
  {
    var camel = char.ToLowerInvariant(propName[0]) + propName[1..];
    var spaced = Grid.Columns.PathToLabel(camel, entityType);
    if (spaced.EndsWith(" Id", StringComparison.Ordinal) && spaced.Length > 3)
    {
      spaced = spaced[..^3];
    }

    var fallback = modL.TextOr(propName, modL[spaced]);
    return feat != null
      ? ScreenBuilderHelpers.ResolveFieldLabel(modL, feat, camel, fallback)
      : fallback;
  }

  public static string? FormatCompactAuditJson(string? targetJson, string? compareJson, bool onlyDiff)
  {
    if (string.IsNullOrWhiteSpace(targetJson))
    {
      return targetJson;
    }

    try
    {
      using var targetDoc = JsonDocument.Parse(targetJson);
      if (targetDoc.RootElement.ValueKind != JsonValueKind.Object)
      {
        return UnescapeJsonText(targetJson);
      }

      JsonDocument? compareDoc = null;
      if (onlyDiff && !string.IsNullOrWhiteSpace(compareJson))
      {
        try
        {
          compareDoc = JsonDocument.Parse(compareJson);
        }
        catch
        {
        }
      }

      using (compareDoc)
      {
        var hasCompareObj = compareDoc?.RootElement.ValueKind == JsonValueKind.Object;
        var filtered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var prop in targetDoc.RootElement.EnumerateObject())
        {
          if (prop.Name is "Id" or "TenantId" or "Ver" or "CreatedAt" or "UpdatedAt")
          {
            continue;
          }

          if (onlyDiff && hasCompareObj)
          {
            if (compareDoc!.RootElement.TryGetProperty(prop.Name, out var otherVal) &&
                string.Equals(prop.Value.GetRawText(), otherVal.GetRawText(), StringComparison.Ordinal))
            {
              continue;
            }
          }
          else if (!onlyDiff && prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
          {
            continue;
          }

          filtered[prop.Name] = prop.Value;
        }

        if (filtered.Count > 0)
        {
          return JsonSerializer.Serialize(filtered, UnescapedJsonOptions);
        }
      }
    }
    catch
    {
    }

    return UnescapeJsonText(targetJson);
  }

  public static string? UnescapeJsonText(string? json)
  {
    if (string.IsNullOrEmpty(json) || !json.Contains("\\u", StringComparison.OrdinalIgnoreCase))
    {
      return json;
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      return JsonSerializer.Serialize(doc.RootElement, UnescapedJsonOptions);
    }
    catch
    {
      return Regex.Unescape(json);
    }
  }

  public static string FormatCorrelationLabel(string correlationId, ILocalizer loc)
  {
    if (TryParseAgentCorrelation(correlationId, out var code, out var batchCount))
    {
      return batchCount is > 1
        ? loc["ChangeAgentBatchBadge", batchCount.Value, code]
        : loc["ChangeAgentSingleBadge", code];
    }

    return correlationId;
  }

  public static (string? Badge, string? Color) ResolveSourceBadge(string? correlationId, ILocalizer loc)
  {
    if (string.IsNullOrWhiteSpace(correlationId) || !TryParseAgentCorrelation(correlationId, out var code, out var batchCount))
    {
      return (null, null);
    }

    return batchCount is > 1
      ? (loc["ChangeAgentBatchBadge", batchCount.Value, code], UiColor.Violet)
      : (loc["ChangeAgentSingleBadge", code], UiColor.Blue);
  }

  public static bool TryParseAgentCorrelation(string correlationId, out string code, out int? batchCount)
  {
    code = string.Empty;
    batchCount = null;

    if (correlationId.StartsWith("agent-batch:", StringComparison.OrdinalIgnoreCase))
    {
      var rest = correlationId["agent-batch:".Length..];
      var sep = rest.IndexOf(':');
      if (sep > 0)
      {
        code = rest[..sep];
        if (int.TryParse(rest[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
          batchCount = parsed;
        }
      }
      else
      {
        code = rest;
      }

      return !string.IsNullOrWhiteSpace(code);
    }

    if (correlationId.StartsWith("agent:", StringComparison.OrdinalIgnoreCase))
    {
      code = correlationId["agent:".Length..];
      return !string.IsNullOrWhiteSpace(code);
    }

    return false;
  }

  public static string? TryExtractRecordSummary(string? json)
  {
    if (string.IsNullOrWhiteSpace(json))
    {
      return null;
    }

    try
    {
      using var doc = JsonDocument.Parse(json);
      if (doc.RootElement.ValueKind != JsonValueKind.Object)
      {
        return null;
      }

      foreach (var key in SummaryPropertyNames)
      {
        if (doc.RootElement.TryGetProperty(key, out var prop) &&
            prop.ValueKind == JsonValueKind.String &&
            prop.GetString() is { Length: > 0 } val)
        {
          var trimmed = val.Trim();
          return trimmed.Length > 48 ? $"{trimmed[..45]}…" : trimmed;
        }
      }
    }
    catch
    {
    }

    return null;
  }
}
