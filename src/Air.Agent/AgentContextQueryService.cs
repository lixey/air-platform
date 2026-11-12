using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentRowValueHelpers;
using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent;

public static class AgentContextQueryService
{
  public static async Task<object?> BuildContextAsync(
    IServiceProvider services,
    string? userMessage = null,
    CancellationToken ct = default,
    bool includeReports = true,
    bool includeRecentRecords = true)
  {
    var options = services.GetService<AgentOptions>() ?? new AgentOptions();
    if (!HasDatabaseRegistered(services))
    {
      return new
      {
        today = ResolveTodayString(services, app: null),
        forms = Array.Empty<object>(),
        dictionaries = new Dictionary<string, object>(),
        largeDictionaries = new Dictionary<string, object>(),
        recentRecords = new Dictionary<string, object>()
      };
    }

    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var today = ResolveTodayString(services, defaultScope.App);
    var descriptors = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App);
    var refServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var forms = new List<object>();

    foreach (var desc in descriptors)
    {
      await using var formScope = AgentModuleScope.OpenForService(services, desc.FormServiceType, defaultScope.Db, defaultScope.App);
      if (ModuleCatalog.Instance.CreateServiceInstance(desc.FormServiceType, formScope.Db, formScope.App) is not IFormService formInst ||
          !formInst.Feature.AllowsAgentWrite ||
          !AgentFeatureRegistry.CanEditFeature(formInst.Feature, formScope.App))
      {
        continue;
      }

      var defaultValues = await GetDefaultFormValuesAsync(formInst, ct);
      var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
      var editableFields = BuildFieldSchemaList(elements, defaultValues, refServices);
      var childSchemas = await BuildChildSchemasAsync(
        children, formInst.Feature, desc.ServicePath, refServices, services, formScope.Db, formScope.App, ct);
      var (statuses, transitions) = await AgentFlowHelper.DiscoverFeatureFlowAsync(
        formInst.Feature, services, formScope.Db, formScope.App, ct);

      forms.Add(new
      {
        service = desc.ServicePath,
        label = desc.Label,
        title = formInst.GetTitle(),
        acceptsFiles = children.Any(c => c.IsFiles) ? true : (bool?)null,
        fields = editableFields,
        children = childSchemas.Count > 0 ? childSchemas : null,
        lines = childSchemas.FirstOrDefault(),
        statuses = statuses.Count > 0 ? statuses.Select(s => new { id = s.Id, name = s.Name }).ToList() : null,
        transitions = transitions.Count > 0 ? MapTransitionsSchema(transitions) : null
      });
    }

    var searchTokens = ExtractSearchTokens(userMessage);
    var (dictionaries, largeDictionaries) = await AgentContextDictionaryLoader.LoadSmartDictionariesAsync(
      refServices, options.MaxFullDictionarySize, searchTokens, services, defaultScope.Db, defaultScope.App, ct);

    var recentRecords = includeRecentRecords
      ? await AgentContextDictionaryLoader.LoadRecentRecordsAsync(
        dictionaries.Keys, ExtractMentionedIds(userMessage), searchTokens, services, defaultScope.Db, defaultScope.App, ct)
      : [];
    var reports = includeReports
      ? await AgentContextDictionaryLoader.LoadReportsSummaryAsync(services, defaultScope.Db, defaultScope.App, ct)
      : [];

    var contextResult = new Dictionary<string, object?>
    {
      ["today"] = today,
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
    CancellationToken ct = default)
  {
    var options = services.GetService<AgentOptions>() ?? new AgentOptions();
    if (!HasDatabaseRegistered(services))
    {
      return new
      {
        today = ResolveTodayString(services, app: null),
        recentRecords = new Dictionary<string, object>()
      };
    }

    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var today = ResolveTodayString(services, defaultScope.App);
    var recentRecords = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgent ||
          feat.ServiceType is not { } svcType ||
          !typeof(IGridService).IsAssignableFrom(svcType))
      {
        continue;
      }

      var gridPath = ServicePathResolver.Resolve(svcType);
      if (recentRecords.ContainsKey(gridPath))
      {
        continue;
      }

      try
      {
        await using var scope = AgentModuleScope.OpenForService(services, svcType, defaultScope.Db, defaultScope.App);
        if (!AgentFeatureRegistry.CanViewFeature(feat, scope.App) ||
            ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App) is not IGridService grid)
        {
          continue;
        }

        var page = await grid.GetDataAsync(new ListRequest { Page = 0, PageSize = 150 }, ct);
        if (page.Total <= options.MaxFullDictionarySize && page.Rows.Count <= options.MaxFullDictionarySize)
        {
          continue;
        }

        var rows = new List<Dictionary<string, object?>>(page.Rows.Count);
        foreach (var rawRow in page.Rows)
        {
          if (CleanGridRow(rawRow, stripZeroDecimals: true) is { } clean)
          {
            rows.Add(clean);
          }
        }

        recentRecords[gridPath] = rows;
      }
      catch { }
    }

    return new { today, recentRecords };
  }

  public static Task<(
    Dictionary<string, List<Dictionary<string, object?>>> Dictionaries,
    Dictionary<string, object> LargeDictionaries)> LoadSmartDictionariesAsync(
    IEnumerable<string> refServices,
    int maxFullSize,
    IReadOnlyList<string> searchTokens,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct) =>
    AgentContextDictionaryLoader.LoadSmartDictionariesAsync(refServices, maxFullSize, searchTokens, app.Services, db, app, ct);

  public static Task<(
    Dictionary<string, List<Dictionary<string, object?>>> Dictionaries,
    Dictionary<string, object> LargeDictionaries)> LoadSmartDictionariesAsync(
    IEnumerable<string> refServices,
    int maxFullSize,
    IReadOnlyList<string> searchTokens,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct) =>
    AgentContextDictionaryLoader.LoadSmartDictionariesAsync(refServices, maxFullSize, searchTokens, rootServices, fallbackDb, fallbackApp, ct);

  public static Task<Dictionary<string, object?>?> FindOrSearchReferenceRowAsync(
    string referenceService,
    FilterNode? referenceFilter,
    string searchName,
    bool allowFuzzy,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct,
    IReadOnlyList<string>? labelPaths = null) =>
    AgentContextDictionaryLoader.FindOrSearchReferenceRowAsync(
      referenceService, referenceFilter, searchName, allowFuzzy, dictionaries, app.Services, db, app, ct, labelPaths);

  public static Task<Dictionary<string, object?>?> EnsureReferenceIdLoadedAsync(
    string referenceService,
    long id,
    Dictionary<string, List<Dictionary<string, object?>>> dictionaries,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct) =>
    AgentContextDictionaryLoader.EnsureReferenceIdLoadedAsync(referenceService, id, dictionaries, app.Services, db, app, ct);

  public static Task<object?> QueryRecordsAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct) =>
    AgentRecordQueryExecutor.QueryRecordsAsync(args, services, ct);

  public static async Task<Dictionary<string, object?>> GetExistingFormValuesAsync(
    IFormService formInst,
    long recordId,
    CancellationToken ct) =>
    (await formInst.PrepareDraftAsync(recordId, ct: ct)).Values;

  public static async Task<Dictionary<string, object?>> GetDefaultFormValuesAsync(
    IFormService formInst,
    CancellationToken ct) =>
    (await formInst.PrepareDraftAsync(recordId: null, ct: ct)).Values;

  internal static string ResolveTodayString(IServiceProvider services, PlatformAppService? app)
  {
    if (app != null)
    {
      return app.Time.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
    var tzId = services.GetService<McpExecutionBridge>()?.Active?.TimeZone
      ?? services.GetService<IUserContext>()?.TimeZone;
    var utcNow = timeProvider.GetUtcNow();
    if (!string.IsNullOrWhiteSpace(tzId))
    {
      try
      {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var local = TimeZoneInfo.ConvertTime(utcNow, tz);
        return DateOnly.FromDateTime(local.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      }
      catch { }
    }

    return DateOnly.FromDateTime(utcNow.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  }

  internal static List<object> MapTransitionsSchema(IReadOnlyList<AgentFlowTransitionDescriptor> transitions) =>
    transitions.Select(t => (object)new
    {
      handle = t.Name,
      title = t.Label,
      fromStatuses = t.FromStatuses,
      fromStatusNames = t.FromStatusNames,
      toStatus = t.ToStatus,
      toStatusName = t.ToStatusName,
      right = t.RequiredRight,
      askFields = t.AskElements.Count > 0
        ? t.AskElements.Where(AgentFeatureRegistry.IsEditableElement).Select(el => new
        {
          path = el.Path,
          label = el.Label,
          type = el.Type,
          required = el.IsRequired ? true : (bool?)null,
          referenceService = (el.Model as FormSelectModel)?.ReferenceService
        }).ToList()
        : null
    }).ToList();

  private static bool HasDatabaseRegistered(IServiceProvider services) =>
    services.GetService<DbScope>() != null || services.GetService<PlatformDbContext>() != null;

  private static List<object> BuildFieldSchemaList(
    IReadOnlyList<FormElementDto> elements,
    Dictionary<string, object?> defaultValues,
    HashSet<string> refServices)
  {
    var editableFields = new List<object>();
    foreach (var el in elements.Where(AgentFeatureRegistry.IsEditableElement))
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
        multiple = select?.IsMultiple == true || el.ValueKind == FormValueKind.List ? true : (bool?)null,
        referenceService = select?.ReferenceService,
        referenceFilter = refFilterMap.Count > 0 ? refFilterMap : null,
        options = select?.Options?.Select(o => new { id = o.Id, name = o.Name }).ToList(),
        defaultValue = defaultVal
      });
    }

    return editableFields;
  }

  private static async Task<List<object>> BuildChildSchemasAsync(
    IReadOnlyList<ChildGridBuilder> children,
    Feature parentFeature,
    string parentServicePath,
    HashSet<string> refServices,
    IServiceProvider services,
    PlatformDbContext fallbackDb,
    PlatformAppService fallbackApp,
    CancellationToken ct)
  {
    var result = new List<object>();
    foreach (var ec in AgentFeatureRegistry.ResolveEditableChildGrids(children, parentFeature, services, fallbackDb, fallbackApp))
    {
      await using var childScope = AgentModuleScope.OpenForService(services, ec.ChildFormType, fallbackDb, fallbackApp);
      if (ModuleCatalog.Instance.CreateServiceInstance(ec.ChildFormType, childScope.Db, childScope.App) is not IFormService childForm)
      {
        continue;
      }

      childForm.SetRequest(new FormRequest { ParentService = parentServicePath });
      var childDefaults = await GetDefaultFormValuesAsync(childForm, ct);
      var (childElements, _) = await childForm.GetElementsMetadataAsync(ct);
      var childFields = BuildFieldSchemaList(childElements, childDefaults, refServices);

      result.Add(new
      {
        key = ec.ChildKey,
        service = ec.ChildFormService,
        gridService = ec.ChildGridService,
        label = ec.Title,
        fields = childFields
      });
    }

    return result;
  }

  private static IReadOnlyList<string> ExtractSearchTokens(string? message)
  {
    if (string.IsNullOrWhiteSpace(message))
    {
      return [];
    }

    var list = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (Match m in Regex.Matches(message, @"[\p{L}\p{N}\-]{3,}"))
    {
      var word = m.Value.Trim('-');
      if (word.Length >= 3 && !decimal.TryParse(word, NumberStyles.Any, CultureInfo.InvariantCulture, out _) && seen.Add(word))
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
