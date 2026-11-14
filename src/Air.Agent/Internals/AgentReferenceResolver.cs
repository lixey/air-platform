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

  public static async Task<(
    Dictionary<string, object?> Values,
    HashSet<string> ExplicitFields,
    HashSet<string> SkipValidationPaths,
    Dictionary<string, List<Dictionary<string, object?>>> Dictionaries)> ParseAskInputsAsync(
    ProposalBuildContext ctx,
    JsonElement args,
    JsonElement valuesSource,
    IReadOnlyList<FormElementDto> askEditable,
    List<PendingNewReference> newReferences,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var askSource = TryGetPropertyIgnoreCase(args, "ask", out var askEl) && askEl.ValueKind == JsonValueKind.Object
      ? askEl
      : valuesSource;

    var refServices = askEditable
      .Select(e => (e.Model as FormSelectModel)?.ReferenceService)
      .Where(s => !string.IsNullOrEmpty(s))
      .Cast<string>()
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var (dictionaries, _) = await AgentContextQueryService.LoadSmartDictionariesAsync(
      refServices, ctx.Options.MaxFullDictionarySize, [], db, app, ct);

    var askValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var explicitAskFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var skipValidationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var el in askEditable)
    {
      var hasProp = TryGetPropertyIgnoreCase(askSource, el.Path, out var rawProp) ||
                    !askSource.Equals(args) && TryGetPropertyIgnoreCase(args, el.Path, out rawProp);
      if (!hasProp || rawProp.ValueKind == JsonValueKind.Undefined)
      {
        continue;
      }

      explicitAskFields.Add(el.Path);
      if (el.Model is FormSelectModel select && !string.IsNullOrEmpty(select.ReferenceService))
      {
        if (AgentStepGraphHelper.TryExtractRefKey(rawProp, out var refKey))
        {
          var refTarget = ctx.FindReferencedProposal(refKey, select.ReferenceService)
            ?? throw new InvalidOperationException(ctx.Localizer["MutationRefused"]);
          askValues[el.Path] = refTarget.RecordId is > 0 ? refTarget.RecordId.Value : null;
          if (refTarget.RecordId is not > 0)
          {
            skipValidationPaths.Add(el.Path);
          }

          newReferences.Add(new PendingNewReference
          {
            FieldPath = el.Path,
            Name = AgentStepGraphHelper.GetProposalShortLabel(refTarget),
            FormService = refTarget.Service,
            ProposalId = refTarget.Id,
            StepKey = refKey,
            StepNumber = refTarget.StepNumber,
            IsExistingRecordEdit = refTarget.RecordId is > 0
          });
          continue;
        }

        var (resolvedId, unresolvedName, _) = await ResolveReferenceValueAsync(
          rawProp, select.ReferenceService, select.ReferenceFilter,
          allowFuzzy: true, allowWrongKindFallback: false,
          dictionaries, db, app, ct, select.ReferenceLabelPaths);
        if (resolvedId == null && !string.IsNullOrWhiteSpace(unresolvedName))
        {
          throw new InvalidOperationException(ctx.Localizer["ReferenceNotFound", unresolvedName, select.ReferenceService]);
        }

        var explicitBadId = rawProp.ValueKind switch
        {
          JsonValueKind.Number when rawProp.TryGetInt64(out var numId) => (long?)numId,
          JsonValueKind.Object => TryGetLong(rawProp, "id"),
          _ => null
        };
        if (resolvedId == null && explicitBadId is > 0)
        {
          throw new InvalidOperationException(ctx.Localizer["ReferenceNotFound", $"#{explicitBadId.Value}", select.ReferenceService]);
        }

        askValues[el.Path] = resolvedId;
      }
      else
      {
        askValues[el.Path] = ConvertJsonValue(rawProp, el.Type);
      }
    }

    return (askValues, explicitAskFields, skipValidationPaths, dictionaries);
  }
}
