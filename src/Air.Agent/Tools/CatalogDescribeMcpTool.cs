using Platform.Agent.Internals;

namespace Platform.Agent.Tools;

public sealed class CatalogDescribeMcpTool : IMcpTool
{
  public string Name => "platform_catalog_describe";

  public string Description =>
    "Describes the editable field schema, child grid schemas, status flow transitions, declared business actions, and defaults of an agent-enabled form/grid/flow, or reads summary KPIs/table of an agent-enabled report.";

  public object InputSchema => new
  {
    type = "object",
    required = new[] { "service" },
    properties = new
    {
      service = new
      {
        type = "string",
        description = "Form, grid/flow/action, or report service path from platform_catalog_list."
      }
    }
  };

  public async Task<object?> ExecuteAsync(JsonElement arguments, IServiceProvider services, CancellationToken ct)
  {
    await using var defaultScope = AgentModuleScope.OpenDefault(services);
    var requested = arguments.TryGetProperty("service", out var sEl) && sEl.ValueKind == JsonValueKind.String
      ? sEl.GetString()?.Trim()
      : null;

    if (string.IsNullOrWhiteSpace(requested))
    {
      throw new InvalidOperationException("Parameter 'service' is required.");
    }

    var descriptors = AgentFeatureRegistry.DiscoverForms(services, defaultScope.Db, defaultScope.App);
    var formDesc = AgentFeatureRegistry.ResolveFormDescriptor(descriptors, requested, services, defaultScope.Db, defaultScope.App);
    if (formDesc != null)
    {
      await using var formScope = AgentModuleScope.OpenForService(services, formDesc.FormServiceType, defaultScope.Db, defaultScope.App);
      if (ModuleCatalog.Instance.CreateServiceInstance(formDesc.FormServiceType, formScope.Db, formScope.App) is IFormService formInst &&
          formInst.Feature.AllowsAgentWrite &&
          AgentFeatureRegistry.CanEditFeature(formInst.Feature, formScope.App))
      {
        formInst.SetRequest(new FormRequest());
        var defaults = await AgentContextQueryService.GetDefaultFormValuesAsync(formInst, ct);
        var (elements, children) = await formInst.GetElementsMetadataAsync(ct);
        var fields = MapEditableElements(elements, defaults);

        var childSchemas = new List<object>();
        foreach (var childInfo in AgentFeatureRegistry.ResolveEditableChildGrids(
                   children, formInst.Feature, services, formScope.Db, formScope.App))
        {
          await using var childScope = AgentModuleScope.OpenForService(
            services, childInfo.ChildFormType, formScope.Db, formScope.App);
          if (ModuleCatalog.Instance.CreateServiceInstance(childInfo.ChildFormType, childScope.Db, childScope.App) is not IFormService childForm)
          {
            continue;
          }

          childForm.SetRequest(new FormRequest { ParentService = formDesc.ServicePath });
          var childDefaults = await AgentContextQueryService.GetDefaultFormValuesAsync(childForm, ct);
          var (childElements, _) = await childForm.GetElementsMetadataAsync(ct);
          childSchemas.Add(new
          {
            key = childInfo.ChildKey,
            service = childInfo.ChildFormService,
            gridService = childInfo.ChildGridService,
            label = childInfo.Title,
            fields = MapEditableElements(childElements, childDefaults)
          });
        }

        var flowDesc = await AgentCatalogInspector.DescribeFeatureFlowAsync(
          formInst.Feature, services, formScope.Db, formScope.App, ct);
        var actions = await AgentCatalogInspector.DescribeFeatureActionsAsync(
          formInst.Feature, formDesc.FormServiceType, services, formScope.Db, formScope.App, ct);

        return new
        {
          kind = "form",
          service = formDesc.ServicePath,
          label = formDesc.Label,
          title = formInst.GetTitle(),
          fields,
          children = childSchemas.Count > 0 ? childSchemas : null,
          lines = childSchemas.FirstOrDefault(),
          statuses = flowDesc is { Statuses.Count: > 0 } ? flowDesc.Statuses.Select(s => new { id = s.Id, name = s.Name }).ToList() : null,
          transitions = flowDesc is { Transitions.Count: > 0 } ? AgentCatalogInspector.MapTransitionsSchema(flowDesc) : null,
          actions = actions.Count > 0 ? AgentCatalogInspector.MapActionsSchema(actions) : null
        };
      }
    }

    var directType = ModuleCatalog.Instance.FindServiceType(requested);
    if (directType != null && typeof(IReportService).IsAssignableFrom(directType))
    {
      await using var reportScope = AgentModuleScope.OpenForService(services, directType, defaultScope.Db, defaultScope.App);
      if (ModuleCatalog.Instance.CreateServiceInstance(directType, reportScope.Db, reportScope.App) is IReportService reportSvc &&
          reportSvc.Feature.AllowsAgent &&
          AgentFeatureRegistry.CanViewFeature(reportSvc.Feature, reportScope.App))
      {
        var data = await reportSvc.GetReportAsync(new ReportRequest { IsDataOnly = true }, ct);
        return new
        {
          kind = "report",
          service = ServicePathResolver.Resolve(directType),
          title = Localizer.Instance.For(reportSvc.Feature.Module)[reportSvc.Feature.Name],
          kpis = data.Kpis,
          tables = data.Tables
        };
      }
    }

    var (targetFeature, targetFormType, targetGridType) = AgentProposalFlowBuilder.ResolveMoveTargetFeature(requested);
    var anchorType = directType ?? targetFormType ?? targetGridType;
    if (targetFeature is { AllowsAgent: true } && anchorType != null)
    {
      await using var svcScope = AgentModuleScope.OpenForService(services, anchorType, defaultScope.Db, defaultScope.App);
      if (AgentFeatureRegistry.CanViewFeature(targetFeature, svcScope.App))
      {
        var flowDesc = await AgentCatalogInspector.DescribeFeatureFlowAsync(
          targetFeature, services, svcScope.Db, svcScope.App, ct);
        var actions = await AgentCatalogInspector.DescribeFeatureActionsAsync(
          targetFeature, anchorType, services, svcScope.Db, svcScope.App, ct);

        if (flowDesc != null || actions.Count > 0 || targetGridType != null)
        {
          var kind = flowDesc != null && actions.Count == 0 ? "flow" : "service";
          return new
          {
            kind,
            service = ServicePathResolver.Resolve(anchorType),
            title = Localizer.Instance.For(targetFeature.Module)[targetFeature.Name],
            statuses = flowDesc is { Statuses.Count: > 0 } ? flowDesc.Statuses.Select(s => new { id = s.Id, name = s.Name }).ToList() : null,
            transitions = flowDesc is { Transitions.Count: > 0 } ? AgentCatalogInspector.MapTransitionsSchema(flowDesc) : null,
            actions = actions.Count > 0 ? AgentCatalogInspector.MapActionsSchema(actions) : null
          };
        }
      }
    }

    throw new InvalidOperationException($"Service '{requested}' was not found or access is denied.");
  }

  private static List<object> MapEditableElements(
    IReadOnlyList<FormElementDto> elements,
    Dictionary<string, object?> defaults) =>
    elements
      .Where(AgentFeatureRegistry.IsEditableElement)
      .Select(el =>
      {
        var select = el.Model as FormSelectModel;
        defaults.TryGetValue(el.Path, out var defaultVal);
        return (object)new
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
}
