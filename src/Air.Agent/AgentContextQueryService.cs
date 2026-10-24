namespace Air.Agent;

public static class AgentContextQueryService
{
  public static async Task<object?> BuildContextAsync(
    IServiceProvider services,
    string? userMessage = null,
    CancellationToken ct = default,
    bool includeReports = true,
    bool includeRecentRecords = true)
  {
    var db = services.GetService<PlatformDbContext>();
    var app = services.GetService<PlatformAppService>();
    var options = services.GetService<AgentOptions>() ?? new AgentOptions();
    if (db == null || app == null)
    {
      return new
      {
        today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
        forms = Array.Empty<object>(),
        dictionaries = new Dictionary<string, object>(),
        largeDictionaries = new Dictionary<string, object>(),
        recentRecords = new Dictionary<string, object>()
      };
    }

    var descriptors = AgentFeatureRegistry.DiscoverForms(db, app);
    var refServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var forms = new List<object>();

    foreach (var desc in descriptors)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(desc.FormServiceType, db, app) is not IFormService formInst ||
          !AgentFeatureRegistry.CanEditFeature(formInst.Feature, app))
      {
        continue;
      }

      var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
      var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
      var defaultValues = await GetDefaultFormValuesAsync(formInst, editableElements, ct);

      var editableFields = new List<object>();
      foreach (var el in editableElements)
      {
        var select = el.Model as FormSelectModel;
        if (!string.IsNullOrEmpty(select?.ReferenceService))
        {
          refServices.Add(select.ReferenceService);
        }

        defaultValues.TryGetValue(el.Path, out var defaultVal);
        var refFilterMap = ExtractEqualityFilterMap(select?.ReferenceFilter);

        editableFields.Add(new
        {
          path = el.Path,
          label = el.Label,
          type = el.Type,
          required = el.IsRequired ? true : (bool?)null,
          multiple = (select?.IsMultiple == true || el.ValueKind == FormValueKind.List) ? true : (bool?)null,
          referenceService = select?.ReferenceService,
          referenceFilter = refFilterMap.Count > 0 ? refFilterMap : null,
          options = select?.Options?.Select(o => new { id = o.Id, name = o.Name }).ToList(),
          defaultValue = defaultVal
        });
      }

      object? lineSchema = null;
      var lineChild = children.FirstOrDefault(c => !c.IsFiles && c.ServiceType != null && typeof(IGridService).IsAssignableFrom(c.ServiceType));
      if (lineChild?.ServiceType != null &&
          ModuleCatalog.Instance.CreateServiceInstance(lineChild.ServiceType, db, app) is IGridService childGrid)
      {
        var childCreate = childGrid.GetCreateForms().FirstOrDefault();
        if (childCreate != null &&
            ModuleCatalog.Instance.CreateServiceInstance(childCreate.FormServiceType, db, app) is IFormService childForm)
        {
          var (childElements, _) = await childForm.GetElementsMetadataAsync(ct);
          var childEditable = childElements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
          var childDefaults = await GetDefaultFormValuesAsync(childForm, childEditable, ct);
          var childFields = new List<object>();
          foreach (var cel in childEditable)
          {
            var cSelect = cel.Model as FormSelectModel;
            if (!string.IsNullOrEmpty(cSelect?.ReferenceService))
            {
              refServices.Add(cSelect.ReferenceService);
            }

            childDefaults.TryGetValue(cel.Path, out var cDefVal);
            childFields.Add(new
            {
              path = cel.Path,
              label = cel.Label,
              type = cel.Type,
              required = cel.IsRequired ? true : (bool?)null,
              referenceService = cSelect?.ReferenceService,
              defaultValue = cDefVal
            });
          }

          lineSchema = new
          {
            service = childCreate.ServicePath,
            label = !string.IsNullOrEmpty(lineChild.DeclaredLabel)
              ? Localizer.Instance.For(formInst.Feature.Module)[lineChild.DeclaredLabel]
              : childCreate.Label,
            fields = childFields
          };
        }
      }

      forms.Add(new
      {
        service = desc.ServicePath,
        label = desc.Label,
        title = formInst.GetTitle(),
        acceptsFiles = children.Any(c => c.IsFiles) ? true : (bool?)null,
        fields = editableFields,
        lines = lineSchema
      });
    }

    var searchTokens = ExtractSearchTokens(userMessage);
    var (dictionaries, largeDictionaries) = await LoadSmartDictionariesAsync(
      refServices,
      options.MaxFullDictionarySize,
      searchTokens,
      db,
      app,
      ct);

    var recentRecords = includeRecentRecords
      ? await LoadRecentRecordsAsync(dictionaries.Keys, userMessage, searchTokens, db, app, ct)
      : [];
    var reports = includeReports ? await LoadReportsSummaryAsync(db, app, ct) : [];

    return new
    {
      today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
      forms,
      dictionaries,
      largeDictionaries = largeDictionaries.Count > 0 ? largeDictionaries : null,
      recentRecords = recentRecords.Count > 0 ? recentRecords : null,
      reports = reports.Count > 0 ? reports : null
    };
  }

  public static async Task<(
    Dictionary<string, List<Dictionary<string, object?>>> Dictionaries,
    Dictionary<string, object> LargeDictionaries)> LoadSmartDictionariesAsync(
    IEnumerable<string> refServices,
    int maxFullSize,
    IReadOnlyList<string> searchTokens,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var dictionaries = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
    var largeDictionaries = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    foreach (var refSvc in refServices)
    {
      var svcType = ModuleCatalog.Instance.FindServiceType(refSvc);
      if (svcType == null || !typeof(IGridService).IsAssignableFrom(svcType))
      {
        continue;
      }

      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(svcType, db, app) is not IGridService grid ||
            !AgentFeatureRegistry.CanViewFeature(grid.Feature, app))
        {
          continue;
        }

        var probePage = await grid.GetDataAsync(new ListRequest { Page = 0, PageSize = maxFullSize + 1 }, ct);
        if (probePage.Total <= maxFullSize && probePage.Rows.Count <= maxFullSize)
        {
          var rows = new List<Dictionary<string, object?>>();
          foreach (var rawRow in probePage.Rows)
          {
            if (CleanGridRow(rawRow) is { } clean)
            {
              rows.Add(clean);
            }
          }

          dictionaries[refSvc] = rows;
        }
        else
        {
          // Dictionary has > 50 rows: only load rows matching search tokens from user prompt (if any)
          var matchedRows = new List<Dictionary<string, object?>>();
          var seenIds = new HashSet<long>();

          foreach (var token in searchTokens.Take(5))
          {
            var searchPage = await grid.GetDataAsync(new ListRequest
            {
              Page = 0,
              PageSize = 15,
              Search = token
            }, ct);

            foreach (var rawRow in searchPage.Rows)
            {
              if (CleanGridRow(rawRow) is { } clean &&
                  TryGetRowId(clean, out var rid) &&
                  seenIds.Add(rid))
              {
                matchedRows.Add(clean);
              }
            }
          }

          foreach (var rawRow in probePage.Rows.Take(25))
          {
            if (CleanGridRow(rawRow) is { } clean &&
                TryGetRowId(clean, out var rid) &&
                seenIds.Add(rid))
            {
              matchedRows.Add(clean);
            }
          }

          dictionaries[refSvc] = matchedRows;
          largeDictionaries[refSvc] = new
          {
            total = probePage.Total,
            loadedMatches = matchedRows.Count,
            note = "Large dictionary (>50 records): pass the exact name in <air_proposal> and the server will automatically find the existing record in DB or propose creating it if not found."
          };
        }
      }
      catch { }
    }

    return (dictionaries, largeDictionaries);
  }

  public static async Task<Dictionary<string, object?>?> FindOrSearchReferenceRowAsync(
    string referenceService,
    FilterNode? referenceFilter,
    string searchName,
    bool allowFuzzy,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
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
    var inMemoryMatch = FindDictionaryRowByName(filteredRows, cleanName, allowFuzzy);
    if (inMemoryMatch != null)
    {
      return inMemoryMatch;
    }

    // Search DB directly (essential for large dictionaries > 50 rows that were not preloaded in full)
    var svcType = ModuleCatalog.Instance.FindServiceType(referenceService);
    if (svcType != null && typeof(IGridService).IsAssignableFrom(svcType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(svcType, db, app) is IGridService grid &&
            AgentFeatureRegistry.CanViewFeature(grid.Feature, app))
        {
          var searchPage = await grid.GetDataAsync(new ListRequest
          {
            Page = 0,
            PageSize = 25,
            Search = cleanName
          }, ct);

          foreach (var rawRow in searchPage.Rows)
          {
            if (CleanGridRow(rawRow) is { } clean &&
                TryGetRowId(clean, out var rid) &&
                allRows.All(r => !TryGetRowId(r, out var existingId) || existingId != rid))
            {
              allRows.Add(clean);
            }
          }

          filteredRows = FilterRowsByReferenceFilter(allRows, referenceFilter);
          return FindDictionaryRowByName(filteredRows, cleanName, allowFuzzy);
        }
      }
      catch { }
    }

    return null;
  }

  public static async Task<Dictionary<string, object?>?> EnsureReferenceIdLoadedAsync(
    string referenceService,
    long id,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
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

    var svcType = ModuleCatalog.Instance.FindServiceType(referenceService);
    if (svcType != null && typeof(IGridService).IsAssignableFrom(svcType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(svcType, db, app) is IGridService grid &&
            AgentFeatureRegistry.CanViewFeature(grid.Feature, app))
        {
          var rawRow = await grid.ReadRowAsync(id, ct);
          if (CleanGridRow(rawRow) is { } clean)
          {
            rows.Add(clean);
            return clean;
          }
        }
      }
      catch { }
    }

    return null;
  }

  private static async Task<Dictionary<string, List<Dictionary<string, object?>>>> LoadRecentRecordsAsync(
    IEnumerable<string> existingDictionaryKeys,
    string? userMessage,
    IReadOnlyList<string> searchTokens,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var dictSet = new HashSet<string>(existingDictionaryKeys, StringComparer.OrdinalIgnoreCase);
    var result = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
    var mentionedIds = ExtractMentionedIds(userMessage);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgent ||
          !AgentFeatureRegistry.CanEditFeature(feat, app) ||
          !AgentFeatureRegistry.CanViewFeature(feat, app) ||
          feat.ServiceType == null ||
          !typeof(IGridService).IsAssignableFrom(feat.ServiceType))
      {
        continue;
      }

      var gridPath = ServicePathResolver.Resolve(feat.ServiceType);
      if (dictSet.Contains(gridPath) || result.ContainsKey(gridPath))
      {
        continue;
      }

      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(feat.ServiceType, db, app) is IGridService grid)
        {
          var rows = new List<Dictionary<string, object?>>();
          var seenIds = new HashSet<long>();

          // If the user explicitly mentioned a record ID (e.g. #42 or id 42), make sure it's loaded
          foreach (var id in mentionedIds)
          {
            try
            {
              var byId = await grid.ReadRowAsync(id, ct);
              if (CleanGridRow(byId, stripZeroMoney: true) is { } cleanIdRow &&
                  TryGetRowId(cleanIdRow, out var rid) &&
                  seenIds.Add(rid))
              {
                rows.Add(cleanIdRow);
              }
            }
            catch { }
          }

          var page = await grid.GetDataAsync(new ListRequest { Page = 0, PageSize = 15 }, ct);
          foreach (var rawRow in page.Rows)
          {
            if (CleanGridRow(rawRow, stripZeroMoney: true) is { } clean &&
                (!TryGetRowId(clean, out var rid) || seenIds.Add(rid)))
            {
              rows.Add(clean);
            }
          }

          // Also search by keywords if total > 15 so older records mentioned by name are found
          if (page.Total > 15 && searchTokens.Count > 0)
          {
            foreach (var token in searchTokens.Take(3))
            {
              var searchPage = await grid.GetDataAsync(new ListRequest
              {
                Page = 0,
                PageSize = 10,
                Search = token
              }, ct);

              foreach (var rawRow in searchPage.Rows)
              {
                if (CleanGridRow(rawRow, stripZeroMoney: true) is { } clean &&
                    TryGetRowId(clean, out var rid) &&
                    seenIds.Add(rid))
                {
                  rows.Add(clean);
                }
              }
            }
          }

          result[gridPath] = rows;
        }
      }
      catch { }
    }

    return result;
  }

  private static async Task<Dictionary<string, object>> LoadReportsSummaryAsync(
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct)
  {
    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!AgentFeatureRegistry.CanViewFeature(feat, app) ||
          feat.ServiceType == null ||
          !typeof(IReportService).IsAssignableFrom(feat.ServiceType))
      {
        continue;
      }

      var reportPath = ServicePathResolver.Resolve(feat.ServiceType);
      if (result.ContainsKey(reportPath))
      {
        continue;
      }

      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(feat.ServiceType, db, app) is IReportService reportSvc)
        {
          var data = await reportSvc.GetReportAsync(new ReportRequest { IsDataOnly = true }, ct);
          result[reportPath] = new
          {
            title = Localizer.Instance.For(feat.Module)[feat.Name],
            activeFilters = data.ActiveFilters,
            kpis = data.Kpis,
            table = data.Table == null
              ? null
              : new
              {
                title = data.Table.Title,
                columns = data.Table.Columns,
                rows = data.Table.Rows.Take(20).ToList(),
                bottomRows = data.Table.BottomRows
              }
          };
        }
      }
      catch { }
    }

    return result;
  }

  public static async Task<object?> QueryRecordsAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct)
  {
    var db = services.GetRequiredService<PlatformDbContext>();
    var app = services.GetRequiredService<PlatformAppService>();
    var descriptors = AgentFeatureRegistry.DiscoverForms(db, app);

    var requestedService = TryGetString(args, "service") ?? TryGetString(args, "kind");
    var recordId = TryGetRecordId(args);
    var search = TryGetString(args, "search");
    var limit = (int)Math.Clamp(TryGetLong(args, "limit") ?? 25, 1, 100);

    var targetDesc = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, db, app);

    if (recordId is > 0)
    {
      var baseDesc = targetDesc ?? descriptors.FirstOrDefault()
        ?? throw new InvalidOperationException("No agent-enabled forms are registered.");
      var resolvedDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(descriptors, baseDesc, requestedService, recordId.Value, db, app, ct);
      if (ModuleCatalog.Instance.CreateServiceInstance(resolvedDesc.FormServiceType, db, app) is not IFormService formInst ||
          !AgentFeatureRegistry.CanViewFeature(formInst.Feature, app))
      {
        throw new InvalidOperationException($"Access denied or form service '{resolvedDesc.ServicePath}' not available.");
      }

      var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
      var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
      var values = await GetExistingFormValuesAsync(formInst, recordId.Value, editableElements, ct);

      var existingLines = new List<Dictionary<string, object?>>();
      var lineChild = children.FirstOrDefault(c => !c.IsFiles && c.ServiceType != null && typeof(IGridService).IsAssignableFrom(c.ServiceType));
      if (lineChild?.ServiceType != null &&
          ModuleCatalog.Instance.CreateServiceInstance(lineChild.ServiceType, db, app) is IGridService childGrid)
      {
        var page = await childGrid.GetDataAsync(new GridRequest { ParentId = recordId.Value, Page = 0, PageSize = 200, IsDataOnly = true }, ct);
        foreach (var rawRow in page.Rows)
        {
          if (CleanGridRow(rawRow) is { } cleaned)
          {
            existingLines.Add(cleaned);
          }
        }
      }

      return new
      {
        id = recordId.Value,
        service = resolvedDesc.ServicePath,
        label = resolvedDesc.Label,
        values,
        lines = existingLines
      };
    }

    Type? gridType = null;
    if (!string.IsNullOrWhiteSpace(requestedService))
    {
      var directType = ModuleCatalog.Instance.FindServiceType(requestedService);
      if (directType != null)
      {
        if (typeof(IGridService).IsAssignableFrom(directType))
        {
          gridType = directType;
        }
        else if (typeof(IFormService).IsAssignableFrom(directType) &&
                 ModuleCatalog.Instance.CreateServiceInstance(directType, db, app) is IFormService reqForm &&
                 reqForm.Feature.ServiceType != null &&
                 typeof(IGridService).IsAssignableFrom(reqForm.Feature.ServiceType))
        {
          gridType = reqForm.Feature.ServiceType;
        }
      }
    }

    if (gridType == null && targetDesc != null &&
        ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, db, app) is IFormService tmpForm &&
        tmpForm.Feature.ServiceType != null &&
        typeof(IGridService).IsAssignableFrom(tmpForm.Feature.ServiceType))
    {
      gridType = tmpForm.Feature.ServiceType;
    }

    if (gridType == null ||
        ModuleCatalog.Instance.CreateServiceInstance(gridType, db, app) is not IGridService gridInst ||
        !AgentFeatureRegistry.CanViewFeature(gridInst.Feature, app))
    {
      throw new InvalidOperationException($"Grid service for '{requestedService}' was not found or access is denied.");
    }

    var searchPage = await gridInst.GetDataAsync(new ListRequest
    {
      Page = 0,
      PageSize = limit,
      Search = search
    }, ct);

    var rows = searchPage.Rows
      .Select(r => CleanGridRow(r, stripZeroMoney: true))
      .Where(r => r != null)
      .Cast<Dictionary<string, object?>>()
      .ToList();

    return new
    {
      service = ServicePathResolver.Resolve(gridType),
      total = searchPage.Total,
      rows
    };
  }

  public static async Task<Dictionary<string, object?>> GetExistingFormValuesAsync(
    IFormService formInst,
    long recordId,
    IReadOnlyList<FormElementDto> editableElements,
    CancellationToken ct)
  {
    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    var modelObj = await formInst.GetModelAsync(new FormRequest { Id = recordId }, ct);
    if (modelObj == null)
    {
      return values;
    }

    if (modelObj is FormDataDto dto && dto.Value != null)
    {
      foreach (var (k, v) in dto.Value)
      {
        values[k] = UnwrapReferenceValue(v);
      }

      return values;
    }

    var jsonEl = JsonSerializer.SerializeToElement(modelObj, WebJson);
    if (jsonEl.ValueKind != JsonValueKind.Object)
    {
      return values;
    }

    PopulateValuesFromJsonElement(jsonEl, editableElements, values, ignoreZeroNumbers: false);
    return values;
  }

  public static async Task<Dictionary<string, object?>> GetDefaultFormValuesAsync(
    IFormService formInst,
    IReadOnlyList<FormElementDto> editableElements,
    CancellationToken ct)
  {
    var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    try
    {
      var modelObj = await formInst.GetModelAsync(new FormRequest(), ct);
      if (modelObj == null)
      {
        return defaults;
      }

      if (modelObj is FormDataDto dto && dto.Value != null)
      {
        foreach (var (k, v) in dto.Value)
        {
          defaults[k] = UnwrapReferenceValue(v);
        }

        return defaults;
      }

      var jsonEl = JsonSerializer.SerializeToElement(modelObj, WebJson);
      if (jsonEl.ValueKind != JsonValueKind.Object)
      {
        return defaults;
      }

      PopulateValuesFromJsonElement(jsonEl, editableElements, defaults, ignoreZeroNumbers: true);
    }
    catch
    {
      // Ignore default model resolution errors
    }

    return defaults;
  }

  private static void PopulateValuesFromJsonElement(
    JsonElement jsonEl,
    IReadOnlyList<FormElementDto> editableElements,
    Dictionary<string, object?> target,
    bool ignoreZeroNumbers)
  {
    foreach (var el in editableElements)
    {
      if (!TryGetPropertyIgnoreCase(jsonEl, el.Path, out var prop) ||
          prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
      {
        continue;
      }

      if (el.Model is FormSelectModel select)
      {
        if (select.IsMultiple || el.ValueKind == FormValueKind.List)
        {
          if (prop.ValueKind == JsonValueKind.Array)
          {
            var ids = new List<long>();
            foreach (var item in prop.EnumerateArray())
            {
              if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id) && id > 0)
              {
                ids.Add(id);
              }
            }

            if (!ignoreZeroNumbers || ids.Count > 0)
            {
              target[el.Path] = ids;
            }
          }
        }
        else if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var refId) && refId > 0)
        {
          target[el.Path] = refId;
        }
        else if (prop.ValueKind == JsonValueKind.Object && TryGetLong(prop, "id") is > 0 and var objRefId)
        {
          target[el.Path] = objRefId;
        }

        continue;
      }

      if (el.Type is UiFieldType.Date or UiFieldType.Datetime)
      {
        if (prop.ValueKind == JsonValueKind.String &&
            prop.GetString() is { Length: > 0 } dateStr &&
            !dateStr.StartsWith("0001-01-01", StringComparison.Ordinal))
        {
          target[el.Path] = dateStr;
        }

        continue;
      }

      if (el.Type == UiFieldType.Checkbox && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
      {
        target[el.Path] = prop.GetBoolean();
        continue;
      }

      if (el.Type == UiFieldType.Number && prop.ValueKind == JsonValueKind.Number)
      {
        if (!ignoreZeroNumbers && el.Model is not FormMoneyModel && prop.TryGetInt64(out var lngVal))
        {
          target[el.Path] = lngVal;
        }
        else if (prop.TryGetDecimal(out var decVal) && (!ignoreZeroNumbers || decVal != 0m))
        {
          target[el.Path] = decVal;
        }

        continue;
      }

      if (prop.ValueKind == JsonValueKind.String && prop.GetString() is { Length: > 0 } str)
      {
        target[el.Path] = str;
      }
    }
  }

  private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
  {
    "додай", "додати", "створи", "створити", "зміни", "змінити", "відредагуй", "редагуй",
    "операцію", "операція", "витрату", "витрата", "дохід", "переказ", "запис", "суму", "сума",
    "розбери", "прикріплене", "фото", "чек", "чека", "скріншот", "підготуй", "записи",
    "грн", "uah", "usd", "eur", "на", "в", "у", "за", "від", "до", "та", "і", "й", "або", "що", "це", "як", "по", "із", "зі",
    "add", "create", "edit", "update", "change", "record", "transaction", "expense", "income", "transfer", "amount", "for", "from", "to", "in", "on", "at", "the", "and", "with", "parse", "attached", "photo", "receipt", "image"
  };

  private static IReadOnlyList<string> ExtractSearchTokens(string? message)
  {
    if (string.IsNullOrWhiteSpace(message))
    {
      return [];
    }

    var matches = Regex.Matches(message, @"[\p{L}\p{N}\-]{3,}");
    var list = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (Match m in matches)
    {
      var word = m.Value.Trim('-');
      if (word.Length < 3 || decimal.TryParse(word, out _) || StopWords.Contains(word))
      {
        continue;
      }

      if (seen.Add(word))
      {
        list.Add(word);
      }
    }

    return list;
  }

  private static IReadOnlyList<long> ExtractMentionedIds(string? message)
  {
    if (string.IsNullOrWhiteSpace(message))
    {
      return [];
    }

    var list = new List<long>();
    foreach (Match m in Regex.Matches(message, @"(?:#|№|\bid\s*[:=]?\s*)(\d{1,10})\b", RegexOptions.IgnoreCase))
    {
      if (long.TryParse(m.Groups[1].Value, out var id) && id > 0 && !list.Contains(id))
      {
        list.Add(id);
      }
    }

    return list;
  }
}
