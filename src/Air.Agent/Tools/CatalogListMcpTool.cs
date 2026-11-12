namespace Platform.Agent.Tools;

public sealed class CatalogListMcpTool : IMcpTool
{
  public string Name => "platform_catalog_list";

  public string Description =>
    "Lists agent-enabled forms (read-write), grids (read), and analytical reports (read) permitted for the current user.";

  public object InputSchema => new
  {
    type = "object",
    properties = new { }
  };

  public Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct)
  {
    var db = services.GetRequiredService<PlatformDbContext>();
    var app = services.GetRequiredService<PlatformAppService>();

    var forms = AgentFeatureRegistry.DiscoverForms(db, app)
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
      if (!feat.AllowsAgent || feat.ServiceType == null || !AgentFeatureRegistry.CanViewFeature(feat, app))
      {
        continue;
      }

      var path = ServicePathResolver.Resolve(feat.ServiceType);
      var title = Localizer.Instance.For(feat.Module)[feat.Name];

      if (typeof(IGridService).IsAssignableFrom(feat.ServiceType))
      {
        grids.Add(new
        {
          service = path,
          title,
          canWrite = feat.AllowsAgentWrite && AgentFeatureRegistry.CanEditFeature(feat, app)
        });
      }
      else if (typeof(IReportService).IsAssignableFrom(feat.ServiceType))
      {
        reports.Add(new
        {
          service = path,
          title
        });
      }
    }

    return Task.FromResult<object?>(new
    {
      forms,
      grids,
      reports
    });
  }
}
