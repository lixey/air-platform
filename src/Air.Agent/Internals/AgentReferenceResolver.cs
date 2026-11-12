using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentReferenceResolver
{
  public static async Task<(long? ResolvedId, string? UnresolvedName, Dictionary<string, object?>? ExtraValues)> ResolveReferenceValueAsync(
    JsonElement rawProp,
    string refService,
    FilterNode? refFilter,
    bool allowFuzzy,
    bool allowWrongKindFallback,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct,
    IReadOnlyList<string>? labelPaths = null)
  {
    if (string.IsNullOrEmpty(refService))
    {
      if (rawProp.ValueKind == JsonValueKind.Number && rawProp.TryGetInt64(out var rawId) && rawId > 0)
      {
        return (rawId, null, null);
      }

      if (rawProp.ValueKind == JsonValueKind.Object && TryGetLong(rawProp, "id") is > 0 and var rawObjId)
      {
        return (rawObjId, null, null);
      }

      return (null, null, null);
    }

    if (rawProp.ValueKind == JsonValueKind.Number && rawProp.TryGetInt64(out var numId) && numId > 0)
    {
      await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refService, numId, dictionaries, db, app, ct);
      var allDictRows = dictionaries.TryGetValue(refService, out var rows) ? rows : [];
      var filteredRows = FilterRowsByReferenceFilter(allDictRows, refFilter);
      if (filteredRows.Any(r => TryGetRowId(r, out var rid) && rid == numId))
      {
        return (numId, null, null);
      }

      if (allowWrongKindFallback &&
          allDictRows.FirstOrDefault(r => TryGetRowId(r, out var rid) && rid == numId) is { } wrongKindRow &&
          GetRowDisplayName(wrongKindRow, labelPaths) is { Length: > 0 } wrongName &&
          await AgentContextQueryService.FindOrSearchReferenceRowAsync(
            refService,
            refFilter,
            wrongName,
            allowFuzzy: true,
            dictionaries,
            db,
            app,
            ct,
            labelPaths) is { } sameNameMatch &&
          TryGetRowId(sameNameMatch, out var correctedId))
      {
        return (correctedId, null, null);
      }

      return (null, null, null);
    }

    if (rawProp.ValueKind == JsonValueKind.String && rawProp.GetString()?.Trim() is { Length: > 0 } strVal)
    {
      if (long.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) &&
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refService, parsedId, dictionaries, db, app, ct) != null &&
          (refFilter == null || FilterRowsByReferenceFilter(dictionaries[refService], refFilter).Any(r => TryGetRowId(r, out var rid) && rid == parsedId)))
      {
        return (parsedId, null, null);
      }

      var matched = await AgentContextQueryService.FindOrSearchReferenceRowAsync(
        refService,
        refFilter,
        strVal,
        allowFuzzy,
        dictionaries,
        db,
        app,
        ct,
        labelPaths);
      return matched != null && TryGetRowId(matched, out var mId)
        ? (mId, null, null)
        : (null, strVal, null);
    }

    if (rawProp.ValueKind == JsonValueKind.Object)
    {
      if (TryGetLong(rawProp, "id") is > 0 and var objId &&
          await AgentContextQueryService.EnsureReferenceIdLoadedAsync(refService, objId, dictionaries, db, app, ct) != null &&
          (refFilter == null || FilterRowsByReferenceFilter(dictionaries[refService], refFilter).Any(r => TryGetRowId(r, out var rid) && rid == objId)))
      {
        return (objId, null, null);
      }

      var objName = (labelPaths is { Count: > 0 }
          ? labelPaths.Select(lp => TryGetString(rawProp, lp)?.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s))
          : null)
        ?? TryGetString(rawProp, "name")?.Trim()
        ?? TryGetString(rawProp, "title")?.Trim()
        ?? TryGetString(rawProp, "code")?.Trim();

      if (!string.IsNullOrEmpty(objName))
      {
        var matched = await AgentContextQueryService.FindOrSearchReferenceRowAsync(
          refService,
          refFilter,
          objName,
          allowFuzzy,
          dictionaries,
          db,
          app,
          ct,
          labelPaths);
        if (matched != null && TryGetRowId(matched, out var mId))
        {
          return (mId, null, null);
        }

        var extraValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in rawProp.EnumerateObject())
        {
          if (!prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase) &&
              !prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
          {
            extraValues[prop.Name] = ConvertJsonValue(prop.Value, UiFieldType.Text);
          }
        }

        return (null, objName, extraValues);
      }
    }

    return (null, null, null);
  }
}
