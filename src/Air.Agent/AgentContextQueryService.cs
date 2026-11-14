using Platform.Agent.Internals;
using static Platform.Agent.Internals.AgentRowValueHelpers;

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
        grids = Array.Empty<object>(),
        dictionaries = new Dictionary<string, object>(),
        largeDictionaries = new Dictionary<string, object>(),
        recentRecords = new Dictionary<string, object>()
      };
    }

    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var today = ResolveTodayString(services, defaultScope.App);
    var descriptors = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App);
    var refServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var coveredFeatures = new HashSet<Feature>();
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

      coveredFeatures.Add(formInst.Feature);
      var defaultValues = await GetDefaultFormValuesAsync(formInst, ct);
      var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
      var editableFields = BuildFieldSchemaList(elements, defaultValues, refServices);
      var childSchemas = await BuildChildSchemasAsync(
        children, formInst.Feature, desc.ServicePath, refServices, services, formScope.Db, formScope.App, ct);
      var flowDesc = await AgentCatalogInspector.DescribeFeatureFlowAsync(
        formInst.Feature, services, formScope.Db, formScope.App, ct);
      var actions = await AgentCatalogInspector.DescribeFeatureActionsAsync(
        formInst.Feature, desc.FormServiceType, services, formScope.Db, formScope.App, ct);

      forms.Add(new
      {
        service = desc.ServicePath,
        label = desc.Label,
        title = formInst.GetTitle(),
        acceptsFiles = children.Any(c => c.IsFiles) ? true : (bool?)null,
        fields = editableFields,
        children = childSchemas.Count > 0 ? childSchemas : null,
        lines = childSchemas.FirstOrDefault(),
        statuses = flowDesc is { Statuses.Count: > 0 } ? flowDesc.Statuses.Select(s => new { id = s.Id, name = s.Name }).ToList() : null,
        transitions = flowDesc is { Transitions.Count: > 0 } ? AgentCatalogInspector.MapTransitionsSchema(flowDesc, refServices) : null,
        actions = actions.Count > 0 ? AgentCatalogInspector.MapActionsSchema(actions, refServices) : null
      });
    }

    var grids = new List<object>();
    var actionServices = new List<object>();
    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgent || feat.ServiceType is not { } svcType)
      {
        continue;
      }

      try
      {
        await using var featScope = AgentModuleScope.OpenForService(services, svcType, defaultScope.Db, defaultScope.App);
        if (!AgentFeatureRegistry.CanViewFeature(feat, featScope.App))
        {
          continue;
        }

        if (typeof(IGridService).IsAssignableFrom(svcType))
        {
          grids.Add(new
          {
            service = ServicePathResolver.Resolve(svcType),
            title = Localizer.Instance.For(feat.Module)[feat.Name]
          });
        }

        if (!coveredFeatures.Contains(feat))
        {
          var featActions = await AgentCatalogInspector.DescribeFeatureActionsAsync(
            feat, svcType, services, featScope.Db, featScope.App, ct);
          if (featActions.Count > 0)
          {
            actionServices.Add(new
            {
              service = featActions[0].Service,
              title = Localizer.Instance.For(feat.Module)[feat.Name],
              actions = AgentCatalogInspector.MapActionsSchema(featActions, refServices)
            });
          }
        }
      }
      catch { }
    }

    var searchTokens = ExtractSearchTokens(userMessage);
    var (dictionaries, largeDictionaries) = await AgentContextDictionaryLoader.LoadSmartDictionariesAsync(
      refServices, options.MaxFullDictionarySize, searchTokens, services, defaultScope.Db, defaultScope.App, ct);

    var mentionedRecords = includeRecentRecords
      ? await AgentContextDictionaryLoader.LoadMentionedRecordsAsync(
        dictionaries.Keys, ExtractMentionedIds(userMessage), services, defaultScope.Db, defaultScope.App, ct)
      : [];
    var reports = includeReports
      ? await AgentContextDictionaryLoader.LoadReportsSummaryAsync(services, defaultScope.Db, defaultScope.App, ct)
      : [];

    var contextResult = new Dictionary<string, object?>
    {
      ["today"] = today,
      ["forms"] = forms,
      ["grids"] = grids,
      ["dictionaries"] = dictionaries
    };
    if (actionServices.Count > 0)
    {
      contextResult["actionServices"] = actionServices;
    }

    if (largeDictionaries.Count > 0)
    {
      contextResult["largeDictionaries"] = largeDictionaries;
    }

    if (mentionedRecords.Count > 0)
    {
      contextResult["recentRecords"] = mentionedRecords;
    }

    if (reports.Count > 0)
    {
      contextResult["reports"] = reports;
    }

    return contextResult;
  }

  public static async Task<(string Today, Dictionary<string, List<Dictionary<string, object?>>> MentionedRecords)> BuildDeltaContextAsync(
    IServiceProvider? services,
    string? userMessage = null,
    CancellationToken ct = default)
  {
    if (services == null || !HasDatabaseRegistered(services))
    {
      var fallbackToday = services != null
        ? ResolveTodayString(services, app: null)
        : DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      return (fallbackToday, new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase));
    }

    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var today = ResolveTodayString(services, defaultScope.App);
    var mentionedIds = ExtractMentionedIds(userMessage);
    var mentioned = mentionedIds.Count > 0
      ? await AgentContextDictionaryLoader.LoadMentionedRecordsAsync(
        [], mentionedIds, services, defaultScope.Db, defaultScope.App, ct)
      : new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

    return (today, mentioned);
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
