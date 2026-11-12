using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentStepGraphHelper
{
  public static string? NormalizeStepKey(string? raw)
  {
    if (string.IsNullOrWhiteSpace(raw))
    {
      return null;
    }

    var trimmed = raw.Trim();
    if (trimmed.StartsWith("$ref:", StringComparison.OrdinalIgnoreCase))
    {
      trimmed = trimmed[5..].Trim();
    }

    return trimmed.Length is > 0 and <= 64 ? trimmed : null;
  }

  public static string? TryExtractStepKey(JsonElement args)
  {
    if (args.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    if (TryGetString(args, "stepKey") is { } explicitStepKey)
    {
      return NormalizeStepKey(explicitStepKey);
    }

    var hasNestedValues = args.TryGetProperty("values", out var vProp) && vProp.ValueKind == JsonValueKind.Object;
    if (hasNestedValues && TryGetString(args, "key") is { } topKey)
    {
      return NormalizeStepKey(topKey);
    }

    if (hasNestedValues && TryGetString(vProp, "stepKey") is { } nestedStepKey)
    {
      return NormalizeStepKey(nestedStepKey);
    }

    return null;
  }

  public static bool TryExtractRefKey(JsonElement rawProp, out string refKey)
  {
    refKey = string.Empty;

    if (rawProp.ValueKind == JsonValueKind.String && rawProp.GetString()?.Trim() is { Length: > 5 } str)
    {
      if (str.StartsWith("$ref:", StringComparison.OrdinalIgnoreCase))
      {
        var normalized = NormalizeStepKey(str[5..]);
        if (!string.IsNullOrEmpty(normalized))
        {
          refKey = normalized;
          return true;
        }
      }

      return false;
    }

    if (rawProp.ValueKind == JsonValueKind.Object &&
        (TryGetPropertyIgnoreCase(rawProp, "$ref", out var refEl) ||
         TryGetPropertyIgnoreCase(rawProp, "ref", out refEl) && rawProp.EnumerateObject().Count() == 1) &&
        refEl.ValueKind == JsonValueKind.String &&
        NormalizeStepKey(refEl.GetString()) is { } objRef)
    {
      refKey = objRef;
      return true;
    }

    return false;
  }

  public static HashSet<string> CollectReferencedKeys(JsonElement args)
  {
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (args.ValueKind != JsonValueKind.Object)
    {
      return keys;
    }

    var vProp = TryGetPropertyIgnoreCase(args, "values", out var vp) && vp.ValueKind == JsonValueKind.Object ? vp : args;
    CollectFromObject(vProp, keys);

    if (TryGetPropertyIgnoreCase(args, "ask", out var askProp) && askProp.ValueKind == JsonValueKind.Object)
    {
      CollectFromObject(askProp, keys);
    }

    if ((TryGetPropertyIgnoreCase(args, "dependsOn", out var depProp) ||
         vProp.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(vProp, "dependsOn", out depProp)) &&
        depProp.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in depProp.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String && NormalizeStepKey(item.GetString()) is { } depKey)
        {
          keys.Add(depKey);
        }
      }
    }

    CollectFromRowArrayProperty(args, vProp, "lines", keys);
    CollectFromChildrenContainer(args, keys);
    if (vProp.ValueKind == JsonValueKind.Object && !vProp.Equals(args))
    {
      CollectFromChildrenContainer(vProp, keys);
    }

    return keys;
  }

  private static void CollectFromRowArrayProperty(JsonElement outer, JsonElement inner, string propName, HashSet<string> keys)
  {
    if ((TryGetPropertyIgnoreCase(outer, propName, out var arrProp) ||
         inner.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(inner, propName, out arrProp)) &&
        arrProp.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in arrProp.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.Object)
        {
          CollectFromObject(item, keys);
        }
      }
    }
  }

  private static void CollectFromChildrenContainer(JsonElement container, HashSet<string> keys)
  {
    if (!TryGetPropertyIgnoreCase(container, "children", out var childrenEl))
    {
      return;
    }

    if (childrenEl.ValueKind == JsonValueKind.Array)
    {
      foreach (var groupEl in childrenEl.EnumerateArray())
      {
        if (groupEl.ValueKind == JsonValueKind.Object &&
            TryGetPropertyIgnoreCase(groupEl, "rows", out var rowsEl) &&
            rowsEl.ValueKind == JsonValueKind.Array)
        {
          foreach (var rowEl in rowsEl.EnumerateArray())
          {
            if (rowEl.ValueKind == JsonValueKind.Object)
            {
              CollectFromObject(rowEl, keys);
            }
          }
        }
      }
    }
    else if (childrenEl.ValueKind == JsonValueKind.Object)
    {
      foreach (var prop in childrenEl.EnumerateObject())
      {
        var rowsSource = prop.Value;
        if (rowsSource.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(rowsSource, "rows", out var nestedRows))
        {
          rowsSource = nestedRows;
        }

        if (rowsSource.ValueKind == JsonValueKind.Array)
        {
          foreach (var rowEl in rowsSource.EnumerateArray())
          {
            if (rowEl.ValueKind == JsonValueKind.Object)
            {
              CollectFromObject(rowEl, keys);
            }
          }
        }
      }
    }
  }

  private static void CollectFromObject(JsonElement obj, HashSet<string> keys)
  {
    foreach (var prop in obj.EnumerateObject())
    {
      if (string.Equals(prop.Name, "key", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(prop.Name, "stepKey", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(prop.Name, "sessionId", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(prop.Name, "service", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(prop.Name, "operation", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(prop.Name, "transition", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(prop.Name, "kind", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (TryExtractRefKey(prop.Value, out var rk))
      {
        keys.Add(rk);
      }
      else if (prop.Value.ValueKind == JsonValueKind.Array)
      {
        foreach (var item in prop.Value.EnumerateArray())
        {
          if (TryExtractRefKey(item, out var itemRk))
          {
            keys.Add(itemRk);
          }
        }
      }
    }
  }

  public static string? ExtractInlinePrimaryName(JsonElement el)
  {
    if (el.ValueKind != JsonValueKind.Object)
    {
      return null;
    }

    var source = el.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Object ? v : el;
    foreach (var key in new[] { "name", "title", "code", "label", "fullName", "number" })
    {
      if (TryGetPropertyIgnoreCase(source, key, out var n) &&
          n.ValueKind == JsonValueKind.String &&
          n.GetString()?.Trim() is { Length: > 0 } str)
      {
        return str;
      }
    }

    return null;
  }

  public static List<JsonElement> SortInlineProposalsTopologically(IReadOnlyList<JsonElement> elements) =>
    AgentTopologicalSorter.SortInlineProposalsTopologically(elements);

  public static List<AgentProposalDto> SortStagedProposalsTopologically(
    IReadOnlyList<AgentProposalDto> proposals,
    IReadOnlyDictionary<string, StagedProposalPayload>? payloadsById = null) =>
    AgentTopologicalSorter.SortStagedProposalsTopologically(proposals, payloadsById);

  public static bool AreServicesEntityCompatible(string? referenceService, string? formService)
  {
    if (string.IsNullOrWhiteSpace(referenceService) || string.IsNullOrWhiteSpace(formService))
    {
      return false;
    }

    var gridType = ModuleCatalog.Instance.FindServiceType(referenceService);
    var formType = ModuleCatalog.Instance.FindServiceType(formService);
    var gridEntity = ModuleCatalog.GetServiceEntityType(gridType);
    var formEntity = ModuleCatalog.GetServiceEntityType(formType);
    return gridEntity != null && formEntity != null &&
      (gridEntity == formEntity || gridEntity.IsAssignableFrom(formEntity) || formEntity.IsAssignableFrom(gridEntity));
  }

  public static string GetProposalShortLabel(AgentProposalDto proposal)
  {
    try
    {
      if (proposal.Payload.ValueKind == JsonValueKind.Object &&
          proposal.Payload.Deserialize<StagedProposalPayload>(WebJson) is { } payload)
      {
        foreach (var key in new[] { "name", "title", "code", "label", "fullName", "number" })
        {
          if (payload.Values.TryGetValue(key, out var val) && val?.ToString()?.Trim() is { Length: > 0 } str)
          {
            return str;
          }
        }
      }
    }
    catch { }

    var title = proposal.Title;
    var colonIdx = title.IndexOf(": ", StringComparison.Ordinal);
    return colonIdx > 0 && colonIdx + 2 < title.Length ? title[(colonIdx + 2)..].Trim() : title;
  }
}
