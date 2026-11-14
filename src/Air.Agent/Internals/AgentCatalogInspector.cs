namespace Platform.Agent.Internals;

internal static class AgentCatalogInspector
{
  public static async Task<FlowDescriptionDto?> DescribeFeatureFlowAsync(
    Feature? feature,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct)
  {
    if (feature is not { AllowsAgent: true })
    {
      return null;
    }

    var gridType = feature.ServiceType != null && typeof(IGridService).IsAssignableFrom(feature.ServiceType) ? feature.ServiceType : null;
    var formType = feature.ServiceType != null && typeof(IFormService).IsAssignableFrom(feature.ServiceType)
      ? feature.ServiceType
      : ModuleCatalog.Instance.AllServiceTypes.FirstOrDefault(t =>
        !t.IsAbstract && typeof(IFormService).IsAssignableFrom(t) && ModuleCatalog.Instance.FindFeature(t) == feature);

    var anchorType = formType ?? gridType;
    if (anchorType == null)
    {
      return null;
    }

    try
    {
      await using var scope = AgentModuleScope.OpenForService(rootServices, anchorType, fallbackDb, fallbackApp);
      if (!AgentFeatureRegistry.CanViewFeature(feature, scope.App))
      {
        return null;
      }

      var flow = (formType != null && ModuleCatalog.Instance.CreateServiceInstance(formType, scope.Db, scope.App) is IFormService f
          ? await f.DescribeStatusFlowAsync(ct)
          : null)
        ?? (gridType != null && ModuleCatalog.Instance.CreateServiceInstance(gridType, scope.Db, scope.App) is IGridService g
          ? await g.DescribeStatusFlowAsync(ct)
          : null);

      if (flow == null)
      {
        return null;
      }

      return feature.AllowsAgentWrite
        ? flow
        : new FlowDescriptionDto { StatusPath = flow.StatusPath, Statuses = flow.Statuses, Transitions = [] };
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return null;
    }
  }

  public static async Task<IReadOnlyList<DeclaredActionDescriptorDto>> DescribeFeatureActionsAsync(
    Feature? feature,
    Type? preferredServiceType,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct)
  {
    if (feature is not { AllowsAgent: true })
    {
      return [];
    }

    var candidates = new List<Type>();
    void AddCandidate(Type? t)
    {
      if (t != null && !candidates.Contains(t))
      {
        candidates.Add(t);
      }
    }

    AddCandidate(preferredServiceType);
    if (feature.ServiceType != null &&
        (typeof(IGridService).IsAssignableFrom(feature.ServiceType) || typeof(IFormService).IsAssignableFrom(feature.ServiceType)))
    {
      AddCandidate(feature.ServiceType);
    }

    foreach (var t in ModuleCatalog.Instance.AllServiceTypes)
    {
      if (!t.IsAbstract && typeof(IFormService).IsAssignableFrom(t) && ModuleCatalog.Instance.FindFeature(t) == feature)
      {
        AddCandidate(t);
      }
    }

    if (candidates.Count == 0)
    {
      return [];
    }

    var result = new List<DeclaredActionDescriptorDto>();
    var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var svcType in candidates)
    {
      try
      {
        await using var scope = AgentModuleScope.OpenForService(rootServices, svcType, fallbackDb, fallbackApp);
        if (!AgentFeatureRegistry.CanViewFeature(feature, scope.App))
        {
          continue;
        }

        var inst = ModuleCatalog.Instance.CreateServiceInstance(svcType, scope.Db, scope.App);
        var actions = inst switch
        {
          IFormService form => await form.DescribeDeclaredActionsAsync(automationOnly: true, ct),
          IGridService grid => await grid.DescribeDeclaredActionsAsync(automationOnly: true, ct),
          _ => []
        };

        foreach (var act in actions)
        {
          if (seenHandles.Add(act.Handle))
          {
            result.Add(act);
          }
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
      }
    }

    return result;
  }

  public static List<object> MapTransitionsSchema(FlowDescriptionDto flow, HashSet<string>? refServices = null)
  {
    var statusNames = flow.Statuses.ToDictionary(s => s.Id, s => s.Name);
    return flow.Transitions.Select(t =>
    {
      var askFields = MapAskElements(t.AskElements, refServices);
      var fromNames = t.From
        .Select(id => statusNames.GetValueOrDefault(id) ?? id.ToString(CultureInfo.InvariantCulture))
        .ToList();

      return (object)new
      {
        handle = t.Name,
        title = t.Label,
        fromStatuses = t.From,
        fromStatusNames = fromNames,
        toStatus = t.To,
        toStatusName = t.ToStatusName,
        askFields = askFields.Count > 0 ? askFields : null
      };
    }).ToList();
  }

  public static List<object> MapActionsSchema(IReadOnlyList<DeclaredActionDescriptorDto> actions, HashSet<string>? refServices = null) =>
    actions.Select(a =>
    {
      var askFields = MapAskElements(a.AskElements, refServices);
      return (object)new
      {
        service = a.Service,
        handle = a.Handle,
        title = a.Label,
        selection = a.SelectionMode,
        askFields = askFields.Count > 0 ? askFields : null
      };
    }).ToList();

  private static List<object> MapAskElements(IReadOnlyList<FormElementDto> elements, HashSet<string>? refServices)
  {
    var list = new List<object>();
    foreach (var el in elements.Where(AgentFeatureRegistry.IsEditableElement))
    {
      var select = el.Model as FormSelectModel;
      if (!string.IsNullOrEmpty(select?.ReferenceService))
      {
        refServices?.Add(select.ReferenceService);
      }

      list.Add(new
      {
        path = el.Path,
        label = el.Label,
        type = el.Type,
        required = el.IsRequired ? true : (bool?)null,
        referenceService = select?.ReferenceService,
        options = select?.Options?.Select(o => new { id = o.Id, name = o.Name }).ToList()
      });
    }

    return list;
  }
}
