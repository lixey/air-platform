using Platform.Agent.Internals;

namespace Platform.Agent.Tools;

public sealed class CatalogListMcpTool : IMcpTool
{
  public string Name => "platform_catalog_list";

  public string Description =>
    "Lists agent-enabled forms (read-write), grids (read), status flows, and analytical reports (read) permitted for the current user.";

  public object InputSchema => new
  {
    type = "object",
    properties = new { }
  };

  public async Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct)
  {
    await using var defaultScope = AgentModuleScope.OpenDefault(services);

    var forms = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App)
      .Select(d => new
      {
        service = d.ServicePath,
        label = d.Label
      })
      .ToList();

    var grids = new List<object>();
    var reports = new List<object>();

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

        var path = ServicePathResolver.Resolve(svcType);
        var title = Localizer.Instance.For(feat.Module)[feat.Name];

        if (typeof(IGridService).IsAssignableFrom(svcType))
        {
          var hasFlow = AgentFlowHelper.FindFlowServiceType(feat) != null;
          grids.Add(new
          {
            service = path,
            title,
            canWrite = feat.AllowsAgentWrite && AgentFeatureRegistry.CanEditFeature(feat, featScope.App),
            hasStatusFlow = hasFlow ? true : (bool?)null
          });
        }
        else if (typeof(IReportService).IsAssignableFrom(svcType))
        {
          reports.Add(new
          {
            service = path,
            title
          });
        }
      }
      catch
      {
        // Skip features belonging to unregistered module DbContexts
      }
    }

    return new
    {
      forms,
      grids,
      reports
    };
  }
}
