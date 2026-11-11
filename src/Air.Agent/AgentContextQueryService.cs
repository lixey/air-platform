using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentContextQueryService
{
  public static async Task<object?> BuildContextAsync(
    IServiceProvider services,
    string? userMessage = null,
    CancellationToken ct = default,
    bool includeReports = true,
    bool includeRecentRecords = true
  )
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

      var defaultValues = await GetDefaultFormValuesAsync(formInst, ct);
      var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
      var editableElements = elements.Where(AgentFeatureRegistry.IsEditableElement).ToList();

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

        editableFields.Add(
          new
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
          }
        );
      }

      object? lineSchema = null;
      if (AgentFeatureRegistry.ResolveEditableChildLineGrid(children, db, app) is { } childLineInfo)
      {
        var childDefaults = await GetDefaultFormValuesAsync(childLineInfo.ChildForm, ct);
        var (childElements, _) = await childLineInfo.ChildForm.GetElementsMetadataAsync(ct);
        var childEditable = childElements.Where(AgentFeatureRegistry.IsEditableElement).ToList();
        var childFields = new List<object>();
        foreach (var cel in childEditable)
        {
          var cSelect = cel.Model as FormSelectModel;
          if (!string.IsNullOrEmpty(cSelect?.ReferenceService))
          {
            refServices.Add(cSelect.ReferenceService);
          }

          childDefaults.TryGetValue(cel.Path, out var cDefVal);
          childFields.Add(
            new
            {
              path = cel.Path,
              label = cel.Label,
              type = cel.Type,
              required = cel.IsRequired ? true : (bool?)null,
              referenceService = cSelect?.ReferenceService,
              defaultValue = cDefVal
            }
          );
        }

        lineSchema = new
        {
          service = childLineInfo.ChildCreate.ServicePath,
          label = !string.IsNullOrEmpty(childLineInfo.Child.DeclaredLabel)
            ? Localizer.Instance.For(formInst.Feature.Module)[childLineInfo.Child.DeclaredLabel]
            : childLineInfo.ChildCreate.Label,
          fields = childFields
        };
      }

      forms.Add(
        new
        {
          service = desc.ServicePath,
          label = desc.Label,
          title = formInst.GetTitle(),
          acceptsFiles = children.Any(c => c.IsFiles) ? true : (bool?)null,
          fields = editableFields,
          lines = lineSchema
        }
      );
    }

    var searchTokens = ExtractSearchTokens(userMessage);
    var (dictionaries, largeDictionaries) = await LoadSmartDictionariesAsync(
      refServices,
      options.MaxFullDictionarySize,
      searchTokens,
      db,
      app,
      ct
    );

    var recentRecords = includeRecentRecords
      ? await LoadRecentRecordsAsync(
        dictionaries.Keys,
        userMessage,
        searchTokens,
        db,
        app,
        ct
      )
      : [];
    var reports = includeReports ? await LoadReportsSummaryAsync(db, app, ct) : [];

    var contextResult = new Dictionary<string, object?>
    {
      ["today"] = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
      ["forms"] = forms,
      ["dictionaries"] = dictionaries
    };
    if (largeDictionaries.Count > 0)
    {
      contextResult["largeDictionaries"] = largeDictionaries;
    }

    if (recentRecords.Count > 0)
    {
      contextResult["recentRecords"] = recentRecords;
    }

    if (reports.Count > 0)
    {
      contextResult["reports"] = reports;
    }

    return contextResult;
  }

  public static async Task<object> BuildExtendedWorkspaceRecordsAsync(
    IServiceProvider services,
    CancellationToken ct = default
  )
  {
    var db = services.GetService<PlatformDbContext>();
    var app = services.GetService<PlatformAppService>();
    var options = services.GetService<AgentOptions>() ?? new AgentOptions();
    var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

    if (db == null || app == null)
    {
      return new
      {
        today,
        recentRecords = new Dictionary<string, object>()
      };
    }

    var recentRecords = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
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
      if (recentRecords.ContainsKey(gridPath))
      {
        continue;
      }

      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(feat.ServiceType, db, app) is IGridService grid)
        {
          var page = await grid.GetDataAsync(new ListRequest { Page = 0, PageSize = 150 }, ct);
          if (page.Total <= options.MaxFullDictionarySize && page.Rows.Count <= options.MaxFullDictionarySize)
          {
            continue;
          }

          var rows = new List<Dictionary<string, object?>>(page.Rows.Count);
          foreach (var rawRow in page.Rows)
          {
            if (CleanGridRow(rawRow, stripZeroMoney: true) is { } clean)
            {
              rows.Add(clean);
            }
          }

          recentRecords[gridPath] = rows;
        }
      }
      catch { }
    }

    return new
    {
      today,
      recentRecords
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
    CancellationToken ct
  )
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
            var searchPage = await grid.GetDataAsync(
              new ListRequest
              {
                Page = 0,
                PageSize = 15,
                Search = token
              },
              ct
            );

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
            note =
              "Large dictionary (>50 records): pass the exact name in <air_proposal> and the server will automatically find the existing record in DB or propose creating it if not found."
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
    CancellationToken ct,
    IReadOnlyList<string>? labelPaths = null
  )
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
    var inMemoryMatch = FindDictionaryRowByName(filteredRows, cleanName, allowFuzzy, labelPaths);
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
          labelPaths ??= grid.GetReferenceLabelPaths();
          var searchPage = await grid.GetDataAsync(
            new ListRequest
            {
              Page = 0,
              PageSize = 25,
              Search = cleanName
            },
            ct
          );

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
          return FindDictionaryRowByName(filteredRows, cleanName, allowFuzzy, labelPaths);
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
    CancellationToken ct
  )
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
    CancellationToken ct
  )
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

          var page = await grid.GetDataAsync(new ListRequest { Page = 0, PageSize = 25 }, ct);
          foreach (var rawRow in page.Rows)
          {
            if (CleanGridRow(rawRow, stripZeroMoney: true) is { } clean &&
                (!TryGetRowId(clean, out var rid) || seenIds.Add(rid)))
            {
              rows.Add(clean);
            }
          }

          // Also search by keywords if total > 25 so older records mentioned by name are found
          if (page.Total > 25 && searchTokens.Count > 0)
          {
            foreach (var token in searchTokens.Take(3))
            {
              var searchPage = await grid.GetDataAsync(
                new ListRequest
                {
                  Page = 0,
                  PageSize = 10,
                  Search = token
                },
                ct
              );

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
    CancellationToken ct
  )
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
                rows = data.Table.Rows.Take(20).Select(r => CleanGridRow(r)).Where(r => r != null).ToList(),
                bottomRows = data.Table.BottomRows?.Select(r => CleanGridRow(r)).Where(r => r != null).ToList()
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
    CancellationToken ct
  )
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
      var resolvedDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(
        descriptors,
        baseDesc,
        requestedService,
        recordId.Value,
        db,
        app,
        ct
      );
      if (ModuleCatalog.Instance.CreateServiceInstance(resolvedDesc.FormServiceType, db, app) is not IFormService formInst ||
          !formInst.Feature.AllowsAgent ||
          !AgentFeatureRegistry.CanViewFeature(formInst.Feature, app))
      {
        throw new InvalidOperationException($"Access denied or form service '{resolvedDesc.ServicePath}' not available.");
      }

      var values = await GetExistingFormValuesAsync(formInst, recordId.Value, ct);
      var (_, children) = await formInst.GetElementsMetadataAsync(ct);

      var existingLines = new List<Dictionary<string, object?>>();
      var childGrid = AgentFeatureRegistry.ResolveEditableChildLineGrid(children, db, app)?.ChildGrid;
      if (childGrid == null)
      {
        foreach (var c in children)
        {
          if (!c.IsFiles && c.ServiceType != null && typeof(IGridService).IsAssignableFrom(c.ServiceType) &&
              ModuleCatalog.Instance.CreateServiceInstance(c.ServiceType, db, app) is IGridService cg &&
              AgentFeatureRegistry.CanViewFeature(cg.Feature, app))
          {
            childGrid = cg;
            break;
          }
        }
      }

      if (childGrid != null)
      {
        var page = await childGrid.GetDataAsync(
          new GridRequest { ParentId = recordId.Value, Page = 0, PageSize = 200, IsDataOnly = true },
          ct
        );
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
        !gridInst.Feature.AllowsAgent ||
        !AgentFeatureRegistry.CanViewFeature(gridInst.Feature, app))
    {
      throw new InvalidOperationException($"Grid service for '{requestedService}' was not found or access is denied.");
    }

    var searchPage = await gridInst.GetDataAsync(
      new ListRequest
      {
        Page = 0,
        PageSize = limit,
        Search = search
      },
      ct
    );

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
    CancellationToken ct
  ) =>
    (await formInst.PrepareDraftAsync(recordId, ct: ct)).Values;

  public static async Task<Dictionary<string, object?>> GetDefaultFormValuesAsync(
    IFormService formInst,
    CancellationToken ct
  ) =>
    (await formInst.PrepareDraftAsync(recordId: null, ct: ct)).Values;

  private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
  {
    "додай", "додати", "створи", "створити", "зміни", "змінити", "відредагуй", "редагуй",
    "запис", "записи", "суму", "сума", "розбери", "прикріплене", "фото", "скріншот", "підготуй",
    "на", "в", "у", "за", "від", "до", "та", "і", "й", "або", "що", "це", "як", "по", "із", "зі",
    "add", "create", "edit", "update", "change", "record", "amount", "for", "from", "to", "in", "on", "at", "the", "and", "with", "parse",
    "attached", "photo", "image"
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
