namespace Platform.Agent.Tools;

public sealed class CatalogDescribeMcpTool : IMcpTool
{
  public string Name => "platform_catalog_describe";

  public string Description =>
    "Describes the editable field schema, child lines schema, and defaults of an agent-enabled form, or reads summary KPIs/table of an agent-enabled report.";

  public object InputSchema => new
  {
    type = "object",
    required = new[] { "service" },
    properties = new
    {
      service = new
      {
        type = "string",
        description = "Form or report service path from platform_catalog_list."
      }
    }
  };

  public async Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct)
  {
    var db = services.GetRequiredService<PlatformDbContext>();
    var app = services.GetRequiredService<PlatformAppService>();
    var requested = arguments.TryGetProperty("service", out var sEl) && sEl.ValueKind == JsonValueKind.String
      ? sEl.GetString()?.Trim()
      : null;

    if (string.IsNullOrWhiteSpace(requested))
    {
      throw new InvalidOperationException("Parameter 'service' is required.");
    }

    var descriptors = AgentFeatureRegistry.DiscoverForms(db, app);
    var formDesc = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requested, db, app);
    if (formDesc != null &&
        ModuleCatalog.Instance.CreateServiceInstance(formDesc.FormServiceType, db, app) is IFormService formInst &&
        formInst.Feature.AllowsAgentWrite &&
        AgentFeatureRegistry.CanEditFeature(formInst.Feature, app))
    {
      formInst.SetRequest(new FormRequest());
      var defaults = await AgentContextQueryService.GetDefaultFormValuesAsync(formInst, ct);
      var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
      var fields = elements
        .Where(AgentFeatureRegistry.IsEditableElement)
        .Select(el =>
        {
          var select = el.Model as FormSelectModel;
          defaults.TryGetValue(el.Path, out var defaultVal);
          return new
          {
            path = el.Path,
            label = el.Label,
            type = el.Type,
            required = el.IsRequired ? true : (bool?)null,
            multiple = select?.IsMultiple == true || el.ValueKind == FormValueKind.List ? true : (bool?)null,
            referenceService = select?.ReferenceService,
            defaultValue = defaultVal
          };
        })
        .ToList();

      object? lineSchema = null;
      if (AgentFeatureRegistry.ResolveEditableChildLineGrid(children, db, app) is { } childInfo)
      {
        childInfo.ChildForm.SetRequest(new FormRequest { ParentService = formDesc.ServicePath });
        var childDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(childInfo.ChildForm, ct);
        var (childElements, _) = await childInfo.ChildForm.GetElementsMetadataAsync(ct);
        lineSchema = new
        {
          service = childInfo.ChildCreate.ServicePath,
          fields = childElements
            .Where(AgentFeatureRegistry.IsEditableElement)
            .Select(cel =>
            {
              var cSelect = cel.Model as FormSelectModel;
              childDefaults.TryGetValue(cel.Path, out var cDef);
              return new
              {
                path = cel.Path,
                label = cel.Label,
                type = cel.Type,
                required = cel.IsRequired ? true : (bool?)null,
                referenceService = cSelect?.ReferenceService,
                defaultValue = cDef
              };
            })
            .ToList()
        };
      }

      return new
      {
        kind = "form",
        service = formDesc.ServicePath,
        label = formDesc.Label,
        title = formInst.GetTitle(),
        fields,
        lines = lineSchema
      };
    }

    var directType = ModuleCatalog.Instance.FindServiceType(requested);
    if (directType != null &&
        typeof(IReportService).IsAssignableFrom(directType) &&
        ModuleCatalog.Instance.CreateServiceInstance(directType, db, app) is IReportService reportSvc &&
        reportSvc.Feature.AllowsAgent &&
        AgentFeatureRegistry.CanViewFeature(reportSvc.Feature, app))
    {
      var data = await reportSvc.GetReportAsync(new ReportRequest { IsDataOnly = true }, ct);
      return new
      {
        kind = "report",
        service = ServicePathResolver.Resolve(directType),
        title = Localizer.Instance.For(reportSvc.Feature.Module)[reportSvc.Feature.Name],
        kpis = data.Kpis,
        table = data.Table
      };
    }

    throw new InvalidOperationException($"Service '{requested}' was not found or access is denied.");
  }
}
