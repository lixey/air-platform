using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentContextDictionaryLoader
{
  public static async Task<(
    Dictionary<string, List<Dictionary<string, object?>>> Dictionaries,
    Dictionary<string, object> LargeDictionaries)> LoadSmartDictionariesAsync(
    IEnumerable<string> refServices,
    int maxFullSize,
    IReadOnlyList<string> searchTokens,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
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
        await using var scope = AgentModuleScope.OpenForService(rootServices, svcType, fallbackDb, fallbackApp);
        if (ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App) is not IGridService grid ||
            !AgentFeatureRegistry.CanViewFeature(grid.Feature, scope.App))
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
          var matchedRows = new List<Dictionary<string, object?>>();
          var seenIds = new HashSet<long>();

          foreach (var token in searchTokens.Take(5))
          {
            var searchPage = await grid.GetDataAsync(
              new ListRequest { Page = 0, PageSize = 15, Search = token },
              ct);

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
              "Large dictionary (>50 records): pass the exact name in <ui_proposal> and the server will automatically find the existing record in DB or propose creating it if not found."
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
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
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
    var inMemoryMatch = FindDictionaryRowByName(filteredRows, cleanName, allowFuzzy, labelPaths);
    if (inMemoryMatch != null)
    {
      return inMemoryMatch;
    }

    var svcType = ModuleCatalog.Instance.FindServiceType(referenceService);
    if (svcType != null && typeof(IGridService).IsAssignableFrom(svcType))
    {
      try
      {
        await using var scope = AgentModuleScope.OpenForService(rootServices, svcType, fallbackDb, fallbackApp);
        if (ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App) is IGridService grid &&
            AgentFeatureRegistry.CanViewFeature(grid.Feature, scope.App))
        {
          labelPaths ??= grid.GetReferenceLabelPaths();
          var searchPage = await grid.GetDataAsync(
            new ListRequest { Page = 0, PageSize = 25, Search = cleanName },
            ct);

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
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
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
        await using var scope = AgentModuleScope.OpenForService(rootServices, svcType, fallbackDb, fallbackApp);
        if (ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App) is IGridService grid &&
            AgentFeatureRegistry.CanViewFeature(grid.Feature, scope.App))
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

  public static async Task<Dictionary<string, List<Dictionary<string, object?>>>> LoadRecentRecordsAsync(
    IEnumerable<string> existingDictionaryKeys,
    IReadOnlyList<long> mentionedIds,
    IReadOnlyList<string> searchTokens,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct)
  {
    var dictSet = new HashSet<string>(existingDictionaryKeys, StringComparer.OrdinalIgnoreCase);
    var result = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgent ||
          feat.ServiceType is not { } svcType ||
          !typeof(IGridService).IsAssignableFrom(svcType))
      {
        continue;
      }

      var gridPath = ServicePathResolver.Resolve(svcType);
      if (dictSet.Contains(gridPath) || result.ContainsKey(gridPath))
      {
        continue;
      }

      try
      {
        await using var scope = AgentModuleScope.OpenForService(rootServices, svcType, fallbackDb, fallbackApp);
        if (!AgentFeatureRegistry.CanViewFeature(feat, scope.App) ||
            ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App) is not IGridService grid)
        {
          continue;
        }

        var rows = new List<Dictionary<string, object?>>();
        var seenIds = new HashSet<long>();

        foreach (var id in mentionedIds)
        {
          try
          {
            var byId = await grid.ReadRowAsync(id, ct);
            if (CleanGridRow(byId, stripZeroDecimals: true) is { } cleanIdRow &&
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
          if (CleanGridRow(rawRow, stripZeroDecimals: true) is { } clean &&
              (!TryGetRowId(clean, out var rid) || seenIds.Add(rid)))
          {
            rows.Add(clean);
          }
        }

        if (page.Total > 25 && searchTokens.Count > 0)
        {
          foreach (var token in searchTokens.Take(3))
          {
            var searchPage = await grid.GetDataAsync(
              new ListRequest { Page = 0, PageSize = 10, Search = token },
              ct);

            foreach (var rawRow in searchPage.Rows)
            {
              if (CleanGridRow(rawRow, stripZeroDecimals: true) is { } clean &&
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
      catch { }
    }

    return result;
  }

  public static async Task<Dictionary<string, object>> LoadReportsSummaryAsync(
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct)
  {
    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgent ||
          feat.ServiceType is not { } svcType ||
          !typeof(IReportService).IsAssignableFrom(svcType))
      {
        continue;
      }

      var reportPath = ServicePathResolver.Resolve(svcType);
      if (result.ContainsKey(reportPath))
      {
        continue;
      }

      try
      {
        await using var scope = AgentModuleScope.OpenForService(rootServices, svcType, fallbackDb, fallbackApp);
        if (!AgentFeatureRegistry.CanViewFeature(feat, scope.App) ||
            ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App) is not IReportService reportSvc)
        {
          continue;
        }

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
      catch { }
    }

    return result;
  }
}
