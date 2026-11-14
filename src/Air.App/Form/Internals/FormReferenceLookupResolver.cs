using Platform.App.Internals;

namespace Platform.App.Form.Internals;

internal static class FormReferenceLookupResolver
{
  public static bool IsEditableElement(FormElementDto el) =>
    !el.IsDisabled &&
    !el.IsServerOwned &&
    !el.IsHidden &&
    !el.IsSecret &&
    !string.IsNullOrEmpty(el.Path) &&
    !el.Path.Contains('.') &&
    el.Model is not FormInfoModel;

  public static bool TryExtractSymbolicRefKey(JsonElement prop, out string refKey)
  {
    refKey = string.Empty;
    if (prop.ValueKind == JsonValueKind.String &&
        prop.GetString()?.Trim() is { Length: > 5 } raw &&
        raw.StartsWith("$ref:", StringComparison.OrdinalIgnoreCase))
    {
      refKey = raw[5..].Trim();
      return refKey.Length > 0;
    }

    if (prop.ValueKind == JsonValueKind.Object &&
        FormDraftEngine.TryGetJsonPropertyIgnoreCase(prop, "$ref", out var refProp) &&
        refProp.ValueKind == JsonValueKind.String &&
        refProp.GetString()?.Trim() is { Length: > 0 } objRef)
    {
      refKey = objRef.StartsWith("$ref:", StringComparison.OrdinalIgnoreCase)
        ? objRef[5..].Trim()
        : objRef;
      return refKey.Length > 0;
    }

    return false;
  }

  public static async Task PreloadSmallDictionariesAsync(
    IEnumerable<string> refServices,
    int maxFullSize,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type? referrerType,
    PlatformDbContext db,
    PlatformAppService? app,
    CancellationToken ct)
  {
    foreach (var refSvc in refServices)
    {
      if (string.IsNullOrWhiteSpace(refSvc) || dictionaries.ContainsKey(refSvc))
      {
        continue;
      }

      var grid = TryCreateLookupGrid(refSvc, referrerType, db, app);
      if (grid == null)
      {
        continue;
      }

      try
      {
        var probePage = await grid.GetDataAsync(new ListRequest { Page = 0, PageSize = maxFullSize + 1 }, ct);
        if (probePage.Total <= maxFullSize && probePage.Rows.Count <= maxFullSize)
        {
          var rows = new List<Dictionary<string, object?>>(probePage.Rows.Count);
          foreach (var rawRow in probePage.Rows)
          {
            if (CleanRow(rawRow) is { } clean)
            {
              rows.Add(clean);
            }
          }

          dictionaries[refSvc] = rows;
        }
        else
        {
          dictionaries[refSvc] = [];
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
      }
    }
  }

  public static async Task<Dictionary<string, object?>?> EnsureReferenceIdLoadedAsync(
    string referenceService,
    long id,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type? referrerType,
    PlatformDbContext db,
    PlatformAppService? app,
    CancellationToken ct)
  {
    if (id <= 0 || string.IsNullOrWhiteSpace(referenceService))
    {
      return null;
    }

    if (!dictionaries.TryGetValue(referenceService, out var rows))
    {
      rows = [];
      dictionaries[referenceService] = rows;
    }

    var existing = rows.FirstOrDefault(r => TryGetRowId(r, out var rid) && rid == id);
    if (existing != null)
    {
      return existing;
    }

    var grid = TryCreateLookupGrid(referenceService, referrerType, db, app);
    if (grid == null)
    {
      return null;
    }

    try
    {
      var rowDto = await grid.ReadRowAsync(id, ct);
      if (rowDto != null && CleanRow(rowDto) is { } clean)
      {
        rows.Add(clean);
        return clean;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
    }

    return null;
  }

  public static async Task<Dictionary<string, object?>?> FindOrSearchReferenceRowAsync(
    string referenceService,
    FilterNode? referenceFilter,
    string searchName,
    bool allowFuzzy,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type? referrerType,
    PlatformDbContext db,
    PlatformAppService? app,
    CancellationToken ct,
    IReadOnlyList<string>? labelPaths = null)
  {
    var cleanName = searchName.Trim();
    if (string.IsNullOrEmpty(cleanName))
    {
      return null;
    }

    if (!dictionaries.TryGetValue(referenceService, out var allRows))
    {
      allRows = [];
      dictionaries[referenceService] = allRows;
    }

    var filteredRows = FilterRowsByReferenceFilter(allRows, referenceFilter);
    var inMemoryMatch = FindRowByName(filteredRows, cleanName, allowFuzzy, labelPaths);
    if (inMemoryMatch != null)
    {
      return inMemoryMatch;
    }

    var grid = TryCreateLookupGrid(referenceService, referrerType, db, app);
    if (grid != null)
    {
      try
      {
        labelPaths ??= grid.GetReferenceLabelPaths();
        var searchPage = await grid.GetDataAsync(
          new ListRequest { Page = 0, PageSize = 25, Search = cleanName },
          ct);

        foreach (var rawRow in searchPage.Rows)
        {
          if (CleanRow(rawRow) is { } clean &&
              TryGetRowId(clean, out var rid) &&
              allRows.All(r => !TryGetRowId(r, out var existingId) || existingId != rid))
          {
            allRows.Add(clean);
          }
        }

        filteredRows = FilterRowsByReferenceFilter(allRows, referenceFilter);
        return FindRowByName(filteredRows, cleanName, allowFuzzy, labelPaths);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
      }
    }

    return null;
  }

  public static async Task<(long? ResolvedId, string? UnresolvedName, Dictionary<string, object?>? ExtraValues)> ResolveReferenceValueAsync(
    JsonElement rawProp,
    string refService,
    FilterNode? refFilter,
    bool allowFuzzy,
    bool allowWrongKindFallback,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    Type? referrerType,
    PlatformDbContext db,
    PlatformAppService? app,
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
      await EnsureReferenceIdLoadedAsync(refService, numId, dictionaries, referrerType, db, app, ct);
      var allDictRows = dictionaries.TryGetValue(refService, out var rows) ? rows : [];
      var filteredRows = FilterRowsByReferenceFilter(allDictRows, refFilter);
      if (filteredRows.Any(r => TryGetRowId(r, out var rid) && rid == numId))
      {
        return (numId, null, null);
      }

      if (allowWrongKindFallback &&
          allDictRows.FirstOrDefault(r => TryGetRowId(r, out var rid) && rid == numId) is { } wrongKindRow &&
          GetRowDisplayName(wrongKindRow, labelPaths) is { Length: > 0 } wrongName &&
          await FindOrSearchReferenceRowAsync(refService, refFilter, wrongName, true, dictionaries, referrerType, db, app, ct, labelPaths) is { } sameNameMatch &&
          TryGetRowId(sameNameMatch, out var correctedId))
      {
        return (correctedId, null, null);
      }

      return (null, null, null);
    }

    if (rawProp.ValueKind == JsonValueKind.String && rawProp.GetString()?.Trim() is { Length: > 0 } strVal)
    {
      if (long.TryParse(strVal.TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) &&
          parsedId > 0 &&
          await EnsureReferenceIdLoadedAsync(refService, parsedId, dictionaries, referrerType, db, app, ct) != null &&
          (refFilter == null || FilterRowsByReferenceFilter(dictionaries[refService], refFilter).Any(r => TryGetRowId(r, out var rid) && rid == parsedId)))
      {
        return (parsedId, null, null);
      }

      var matched = await FindOrSearchReferenceRowAsync(refService, refFilter, strVal, allowFuzzy, dictionaries, referrerType, db, app, ct, labelPaths);
      return matched != null && TryGetRowId(matched, out var mId)
        ? (mId, null, null)
        : (null, strVal, null);
    }

    if (rawProp.ValueKind == JsonValueKind.Object)
    {
      if (TryGetLong(rawProp, "id") is > 0 and var objId &&
          await EnsureReferenceIdLoadedAsync(refService, objId, dictionaries, referrerType, db, app, ct) != null &&
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
        var matched = await FindOrSearchReferenceRowAsync(refService, refFilter, objName, allowFuzzy, dictionaries, referrerType, db, app, ct, labelPaths);
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
            extraValues[prop.Name] = ConvertJsonScalar(prop.Value, UiFieldType.Text, null);
          }
        }

        return (null, objName, extraValues);
      }
    }

    return (null, null, null);
  }

  public static object? ConvertJsonScalar(JsonElement prop, string fieldType, FormSelectModel? selectModel)
  {
    if (selectModel is { Options.Count: > 0 } && prop.ValueKind == JsonValueKind.String && prop.GetString()?.Trim() is { Length: > 0 } optText)
    {
      if (long.TryParse(optText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var optId) &&
          selectModel.Options.Any(o => o.Id == optId))
      {
        return optId;
      }

      var matchedOpt = selectModel.Options.FirstOrDefault(o => string.Equals(o.Name, optText, StringComparison.OrdinalIgnoreCase));
      if (matchedOpt != null)
      {
        return matchedOpt.Id;
      }
    }

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

            var clean = str.Trim().Replace(" ", "").Replace("\u00A0", "");
            if (clean.Contains(',') && !clean.Contains('.'))
            {
              clean = clean.Replace(',', '.');
            }

            if (decimal.TryParse(clean, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var sDec))
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

  public static IReadOnlyList<Dictionary<string, object?>> FilterRowsByReferenceFilter(
    IReadOnlyList<Dictionary<string, object?>> rows,
    FilterNode? filter)
  {
    if (filter == null || rows.Count == 0 || !RowsExposeFilterFields(rows, filter))
    {
      return rows;
    }

    return rows.Where(r => FilterNodeEvaluator.Evaluate(filter, field =>
    {
      if (r.TryGetValue(field, out var cellVal) && cellVal != null)
      {
        return UnwrapId(cellVal);
      }

      if (field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && field.Length > 2 &&
          r.TryGetValue(field[..^2], out cellVal) && cellVal != null)
      {
        return UnwrapId(cellVal);
      }

      return null;
    })).ToList();
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
      if (row.TryGetValue(key, out var val) && val?.ToString()?.Trim() is { Length: > 0 } str)
      {
        return str;
      }
    }

    foreach (var (k, v) in row)
    {
      if (v is string s && !string.IsNullOrWhiteSpace(s) &&
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

  public static bool TryGetRowId(Dictionary<string, object?> row, out long id)
  {
    if (row.TryGetValue("id", out var idObj) && TryConvertToLong(idObj, out id))
    {
      return true;
    }

    id = 0;
    return false;
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

  public static Dictionary<string, object?>? CleanRow(RowDto? rowDto)
  {
    if (rowDto == null)
    {
      return null;
    }

    var clean = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    if (rowDto.Id.HasValue)
    {
      clean["id"] = rowDto.Id.Value;
    }

    if (!string.IsNullOrWhiteSpace(rowDto.Open?.Service))
    {
      clean["form"] = rowDto.Open.Service;
    }

    foreach (var (k, v) in rowDto.Cells)
    {
      if (v == null || k.StartsWith('_') ||
          string.Equals(k, "ver", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(k, "unavailable", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(k, "idCover", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(k, "flags", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      clean[k] = v is Reference r && r.Id > 0
        ? new Dictionary<string, object?> { ["id"] = r.Id, ["name"] = r.Name }
        : v;
    }

    return clean;
  }

  private static Grid.IGridService? TryCreateLookupGrid(
    string referenceService,
    Type? referrerType,
    PlatformDbContext db,
    PlatformAppService? app)
  {
    var svcType = ModuleCatalog.Instance.FindServiceType(referenceService);
    if (svcType == null || !typeof(Grid.IGridService).IsAssignableFrom(svcType))
    {
      return null;
    }

    if (referrerType != null)
    {
      ModuleCatalog.EnsureModuleServiceAccessAllowed(referrerType, svcType, "AsReference");
    }

    try
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(svcType, db, app) is Grid.IGridService grid &&
          CanLookupGrid(grid, db, app))
      {
        return grid;
      }
    }
    catch
    {
    }

    return null;
  }

  private static bool CanLookupGrid(Grid.IGridService grid, PlatformDbContext db, PlatformAppService? app) =>
    db.TenantContext.IsSystemScope ||
    ServiceEndpointsExtensions.CanView(grid.Feature, app?.User) ||
    grid.CanLookup(new GridRequest(), app?.User);

  private static object? UnwrapId(object? val) => val switch
  {
    Reference r => r.Id > 0 ? r.Id : null,
    IDictionary<string, object?> dict when dict.TryGetValue("id", out var idObj) && TryConvertToLong(idObj, out var dId) => dId > 0 ? dId : null,
    _ => val
  };

  private static bool RowsExposeFilterFields(IReadOnlyList<Dictionary<string, object?>> rows, FilterNode node) =>
    node switch
    {
      FilterGroup group => group.Items.Count == 0 || group.Items.All(item => RowsExposeFilterFields(rows, item)),
      FilterRule rule when !string.IsNullOrEmpty(rule.Field) =>
        string.Equals(rule.Operator, FilterOperator.IsNull, StringComparison.OrdinalIgnoreCase) ||
        rows.Any(r => r.ContainsKey(rule.Field) ||
                      rule.Field.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && rule.Field.Length > 2 && r.ContainsKey(rule.Field[..^2])),
      _ => false
    };

  private static Dictionary<string, object?>? FindRowByName(
    IReadOnlyList<Dictionary<string, object?>> rows,
    string clean,
    bool allowFuzzy,
    IReadOnlyList<string>? labelPaths)
  {
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
             (rowName.Contains(clean, StringComparison.OrdinalIgnoreCase) || clean.Contains(rowName, StringComparison.OrdinalIgnoreCase));
    });
  }

  public static long? TryGetLong(JsonElement element, string name)
  {
    if (!FormDraftEngine.TryGetJsonPropertyIgnoreCase(element, name, out var prop))
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

  public static string? TryGetString(JsonElement element, string name) =>
    FormDraftEngine.TryGetJsonPropertyIgnoreCase(element, name, out var prop) && prop.ValueKind == JsonValueKind.String
      ? prop.GetString()
      : null;
}
