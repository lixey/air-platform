using static Platform.Agent.Internals.AgentValueHelpers;

namespace Platform.Agent.Internals;

internal static class AgentRecordQueryExecutor
{
  public static async Task<object?> QueryRecordsAsync(
    JsonElement args,
    IServiceProvider services,
    CancellationToken ct)
  {
    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var descriptors = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App);

    var requestedService = TryGetString(args, "service") ?? TryGetString(args, "kind");
    var recordId = TryGetRecordId(args);
    var search = TryGetString(args, "search");
    var limit = (int)Math.Clamp(TryGetLong(args, "limit") ?? 25, 1, 100);
    var pageIndex = (int)Math.Max(0, TryGetLong(args, "page") ?? 0);

    var targetDesc = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requestedService, services, defaultScope.Db, defaultScope.App);

    if (recordId is > 0)
    {
      if (string.IsNullOrWhiteSpace(requestedService))
      {
        throw new InvalidOperationException("Parameter 'service' is required when querying a record by 'id'.");
      }

      if (targetDesc != null)
      {
        var resolvedDesc = await AgentFeatureRegistry.ResolveEditFormDescriptorAsync(
          descriptors, targetDesc, requestedService, recordId.Value, services, defaultScope.Db, defaultScope.App, ct);
        await using var formScope = AgentModuleScope.OpenForService(services, resolvedDesc.FormServiceType, defaultScope.Db, defaultScope.App);
        if (ModuleCatalog.Instance.CreateServiceInstance(resolvedDesc.FormServiceType, formScope.Db, formScope.App) is not IFormService formInst ||
            !formInst.Feature.AllowsAgent ||
            !AgentFeatureRegistry.CanViewFeature(formInst.Feature, formScope.App))
        {
          throw new InvalidOperationException($"Access denied or form service '{resolvedDesc.ServicePath}' not available.");
        }

        formInst.SetRequest(new FormRequest { Id = recordId.Value });
        var values = await AgentContextQueryService.GetExistingFormValuesAsync(formInst, recordId.Value, ct);
        var (_, children) = await formInst.GetElementsMetadataAsync(ct);
        var childRowsByKey = await LoadRecordChildrenAsync(
          children, formInst.Feature, resolvedDesc.ServicePath, recordId.Value, services, formScope.Db, formScope.App, ct);

        return new
        {
          id = recordId.Value,
          service = resolvedDesc.ServicePath,
          label = resolvedDesc.Label,
          values,
          children = childRowsByKey,
          lines = childRowsByKey.Values.FirstOrDefault() ?? []
        };
      }

      var directSvcType = ModuleCatalog.Instance.FindServiceType(requestedService);
      if (directSvcType != null && typeof(IGridService).IsAssignableFrom(directSvcType))
      {
        await using var roScope = AgentModuleScope.OpenForService(services, directSvcType, defaultScope.Db, defaultScope.App);
        if (ModuleCatalog.Instance.CreateServiceInstance(directSvcType, roScope.Db, roScope.App) is IGridService roGrid &&
            roGrid.Feature.AllowsAgent &&
            AgentFeatureRegistry.CanViewFeature(roGrid.Feature, roScope.App))
        {
          var row = await roGrid.ReadRowAsync(recordId.Value, ct)
            ?? throw new InvalidOperationException($"Record #{recordId.Value} was not found in '{requestedService}'.");
          return new
          {
            id = recordId.Value,
            service = ServicePathResolver.Resolve(directSvcType),
            values = CleanGridRow(row) ?? new Dictionary<string, object?>()
          };
        }
      }

      throw new InvalidOperationException($"Service '{requestedService}' was not found or access is denied.");
    }

    var gridType = ResolveTargetGridType(requestedService, targetDesc, services, defaultScope.Db, defaultScope.App) ?? throw new InvalidOperationException($"Grid service for '{requestedService}' was not found or access is denied.");
    await using var gridScope = AgentModuleScope.OpenForService(services, gridType, defaultScope.Db, defaultScope.App);
    if (ModuleCatalog.Instance.CreateServiceInstance(gridType, gridScope.Db, gridScope.App) is not IGridService gridInst ||
        !gridInst.Feature.AllowsAgent ||
        !AgentFeatureRegistry.CanViewFeature(gridInst.Feature, gridScope.App))
    {
      throw new InvalidOperationException($"Grid service for '{requestedService}' was not found or access is denied.");
    }

    var searchPage = await gridInst.GetDataAsync(
      new ListRequest { Page = pageIndex, PageSize = limit, Search = search },
      ct);

    var rows = searchPage.Rows
      .Select(r => CleanGridRow(r, stripZeroDecimals: true))
      .Where(r => r != null)
      .Cast<Dictionary<string, object?>>()
      .ToList();

    return new
    {
      service = ServicePathResolver.Resolve(gridType),
      page = pageIndex,
      total = searchPage.Total,
      rows
    };
  }

  private static async Task<Dictionary<string, List<Dictionary<string, object?>>>> LoadRecordChildrenAsync(
    IReadOnlyList<ChildGridBuilder> children,
    Feature parentFeature,
    string parentServicePath,
    long recordId,
    IServiceProvider services,
    PlatformDbContext fallbackDb,
    PlatformAppService fallbackApp,
    CancellationToken ct)
  {
    var result = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);
    foreach (var c in children)
    {
      if (c.IsFiles || c.ServiceType == null || !typeof(IGridService).IsAssignableFrom(c.ServiceType))
      {
        continue;
      }

      await using var childScope = AgentModuleScope.OpenForService(services, c.ServiceType, fallbackDb, fallbackApp);
      if (ModuleCatalog.Instance.CreateServiceInstance(c.ServiceType, childScope.Db, childScope.App) is not IGridService childGrid ||
          !AgentFeatureRegistry.IsChildAgentReadAllowed(childGrid.Feature, parentFeature) ||
          !AgentFeatureRegistry.CanViewFeature(childGrid.Feature, childScope.App))
      {
        continue;
      }

      var page = await childGrid.GetDataAsync(
        new GridRequest { ParentService = parentServicePath, ParentId = recordId, Page = 0, PageSize = 200, IsDataOnly = true },
        ct);

      var rows = new List<Dictionary<string, object?>>(page.Rows.Count);
      foreach (var rawRow in page.Rows)
      {
        if (CleanGridRow(rawRow) is { } cleaned)
        {
          rows.Add(cleaned);
        }
      }

      var key = !string.IsNullOrWhiteSpace(c.DeclaredKey) ? c.DeclaredKey : c.ServicePath;
      result[key] = rows;
    }

    return result;
  }

  private static Type? ResolveTargetGridType(
    string? requestedService,
    GridCreateDescriptor? targetDesc,
    IServiceProvider services,
    PlatformDbContext fallbackDb,
    PlatformAppService fallbackApp)
  {
    if (!string.IsNullOrWhiteSpace(requestedService))
    {
      var directType = ModuleCatalog.Instance.FindServiceType(requestedService);
      if (directType != null)
      {
        if (typeof(IGridService).IsAssignableFrom(directType))
        {
          return directType;
        }

        if (typeof(IFormService).IsAssignableFrom(directType))
        {
          using var scope = AgentModuleScope.OpenForService(services, directType, fallbackDb, fallbackApp);
          if (ModuleCatalog.Instance.CreateServiceInstance(directType, scope.Db, scope.App) is IFormService reqForm &&
              reqForm.Feature.ServiceType != null &&
              typeof(IGridService).IsAssignableFrom(reqForm.Feature.ServiceType))
          {
            return reqForm.Feature.ServiceType;
          }
        }
      }
    }

    if (targetDesc != null)
    {
      using var scope = AgentModuleScope.OpenForService(services, targetDesc.FormServiceType, fallbackDb, fallbackApp);
      if (ModuleCatalog.Instance.CreateServiceInstance(targetDesc.FormServiceType, scope.Db, scope.App) is IFormService tmpForm &&
          tmpForm.Feature.ServiceType != null &&
          typeof(IGridService).IsAssignableFrom(tmpForm.Feature.ServiceType))
      {
        return tmpForm.Feature.ServiceType;
      }
    }

    return null;
  }
}
