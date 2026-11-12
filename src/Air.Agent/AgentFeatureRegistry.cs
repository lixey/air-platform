using Platform.Agent.Internals;

namespace Platform.Agent;

public static class AgentFeatureRegistry
{
  public static bool CanViewFeature(Feature? feat, PlatformAppService app) =>
    ServiceEndpointsExtensions.CanView(feat, app.User);

  public static bool CanEditFeature(Feature? feat, PlatformAppService app) =>
    ServiceEndpointsExtensions.CanEdit(feat, app.User);

  public static bool IsEditableElement(FormElementDto el) =>
    !string.IsNullOrEmpty(el.Path) &&
    !el.IsDisabled &&
    !el.IsServerOwned &&
    !el.IsHidden &&
    !el.IsSecret &&
    !string.Equals(el.Type, "password", StringComparison.OrdinalIgnoreCase) &&
    el.Model is not FormInfoModel;

  public static IReadOnlyList<GridCreateDescriptor> DiscoverForms(PlatformDbContext db, PlatformAppService app) =>
    DiscoverForms(app.Services, db, app);

  public static IReadOnlyList<GridCreateDescriptor> DiscoverForms(
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null)
  {
    var list = new List<GridCreateDescriptor>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgentWrite || feat.ServiceType == null)
      {
        continue;
      }

      try
      {
        using var scope = AgentModuleScope.OpenForService(rootServices, feat.ServiceType, fallbackDb, fallbackApp);
        if (!CanEditFeature(feat, scope.App))
        {
          continue;
        }

        var inst = ModuleCatalog.Instance.CreateServiceInstance(feat.ServiceType, scope.Db, scope.App);
        if (inst is IGridService grid)
        {
          foreach (var desc in grid.GetAgentForms())
          {
            using var formScope = AgentModuleScope.OpenForService(rootServices, desc.FormServiceType, scope.Db, scope.App);
            if (ModuleCatalog.Instance.CreateServiceInstance(desc.FormServiceType, formScope.Db, formScope.App) is IFormService descForm &&
                (!descForm.Feature.AllowsAgentWrite || !CanEditFeature(descForm.Feature, formScope.App)))
            {
              continue;
            }

            if (seen.Add(desc.ServicePath))
            {
              list.Add(desc);
            }
          }
        }
        else if (inst is IFormService form && form.Feature.AllowsAgentWrite)
        {
          if (seen.Add(form.Service))
          {
            list.Add(new GridCreateDescriptor(feat.ServiceType, form.Service, form.GetTitle(), UiColor.Orange));
          }
        }
      }
      catch
      {
        // Ignore service instantiation errors during discovery
      }
    }

    return list;
  }

  public static string NormalizeServiceKey(string s) =>
    s.Trim().Replace('/', '-').ToLowerInvariant();

  public static GridCreateDescriptor? ResolveFormDescriptor(
    IReadOnlyList<GridCreateDescriptor> descriptors,
    string? query,
    PlatformDbContext? db = null,
    PlatformAppService? app = null) =>
    ResolveFormDescriptor(descriptors, query, app?.Services, db, app);

  public static GridCreateDescriptor? ResolveFormDescriptor(
    IReadOnlyList<GridCreateDescriptor> descriptors,
    string? query,
    IServiceProvider? rootServices,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      return null;
    }

    var q = query.Trim();
    var normQ = NormalizeServiceKey(q);

    var match = descriptors.FirstOrDefault(d => string.Equals(d.ServicePath, q, StringComparison.OrdinalIgnoreCase))
      ?? descriptors.FirstOrDefault(d => string.Equals(NormalizeServiceKey(d.ServicePath), normQ, StringComparison.OrdinalIgnoreCase))
      ?? descriptors.FirstOrDefault(d => string.Equals(d.Label, q, StringComparison.OrdinalIgnoreCase));

    if (match != null || rootServices == null && fallbackDb == null)
    {
      return match;
    }

    var directType = ModuleCatalog.Instance.FindServiceType(q);
    if (directType != null)
    {
      try
      {
        using var scope = AgentModuleScope.OpenForService(rootServices ?? fallbackApp!.Services, directType, fallbackDb, fallbackApp);
        if (typeof(IGridService).IsAssignableFrom(directType) &&
            ModuleCatalog.Instance.CreateServiceInstance(directType, scope.Db, scope.App) is IGridService grid &&
            grid.Feature.AllowsAgentWrite &&
            CanEditFeature(grid.Feature, scope.App))
        {
          var agentForms = grid.GetAgentForms();
          foreach (var create in agentForms)
          {
            var found = descriptors.FirstOrDefault(d => string.Equals(d.ServicePath, create.ServicePath, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
              return found;
            }
          }

          return agentForms.FirstOrDefault();
        }

        if (typeof(IFormService).IsAssignableFrom(directType) &&
            ModuleCatalog.Instance.CreateServiceInstance(directType, scope.Db, scope.App) is IFormService form &&
            form.Feature.AllowsAgentWrite &&
            CanEditFeature(form.Feature, scope.App))
        {
          return new GridCreateDescriptor(directType, form.Service, form.GetTitle(), UiColor.Blue);
        }
      }
      catch { }
    }

    return null;
  }

  public static Task<GridCreateDescriptor> ResolveEditFormDescriptorAsync(
    IReadOnlyList<GridCreateDescriptor> descriptors,
    GridCreateDescriptor fallbackDesc,
    string? requestedService,
    long recordId,
    PlatformDbContext db,
    PlatformAppService app,
    CancellationToken ct) =>
    ResolveEditFormDescriptorAsync(descriptors, fallbackDesc, requestedService, recordId, app.Services, db, app, ct);

  public static async Task<GridCreateDescriptor> ResolveEditFormDescriptorAsync(
    IReadOnlyList<GridCreateDescriptor> descriptors,
    GridCreateDescriptor fallbackDesc,
    string? requestedService,
    long recordId,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp,
    CancellationToken ct)
  {
    Type? gridServiceType = null;
    if (!string.IsNullOrWhiteSpace(requestedService))
    {
      var directType = ModuleCatalog.Instance.FindServiceType(requestedService);
      if (directType != null)
      {
        if (typeof(IGridService).IsAssignableFrom(directType))
        {
          gridServiceType = directType;
        }
        else if (typeof(IFormService).IsAssignableFrom(directType) &&
                 ModuleCatalog.Instance.FindFeature(directType)?.ServiceType is { } ownerType &&
                 typeof(IGridService).IsAssignableFrom(ownerType))
        {
          gridServiceType = ownerType;
        }
      }
    }

    if (gridServiceType == null &&
        ModuleCatalog.Instance.FindFeature(fallbackDesc.FormServiceType)?.ServiceType is { } fallbackGridType &&
        typeof(IGridService).IsAssignableFrom(fallbackGridType))
    {
      gridServiceType = fallbackGridType;
    }

    if (gridServiceType != null)
    {
      try
      {
        await using var scope = AgentModuleScope.OpenForService(rootServices, gridServiceType, fallbackDb, fallbackApp);
        if (ModuleCatalog.Instance.CreateServiceInstance(gridServiceType, scope.Db, scope.App) is IGridService ownerGrid &&
            CanViewFeature(ownerGrid.Feature, scope.App))
        {
          var rowObj = await ownerGrid.ReadRowAsync(recordId, ct);
          if (rowObj?.Open?.Service is { Length: > 0 } rowFormPath)
          {
            var matched = ResolveFormDescriptor(descriptors, rowFormPath, rootServices, scope.Db, scope.App);
            if (matched != null)
            {
              return matched;
            }
          }

          var creates = ownerGrid.GetAgentForms();
          if (creates.Count == 1)
          {
            var singleMatch = ResolveFormDescriptor(descriptors, creates[0].ServicePath, rootServices, scope.Db, scope.App);
            if (singleMatch != null)
            {
              return singleMatch;
            }
          }
        }
      }
      catch { }
    }

    return fallbackDesc;
  }

  public static GridCreateDescriptor? ResolveReferenceCreateForm(
    IReadOnlyList<GridCreateDescriptor> agentForms,
    string referenceService,
    FormElementDto element,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb,
    PlatformAppService? fallbackApp)
  {
    if (element.CreateFormServiceType != null)
    {
      var path = ServicePathResolver.Resolve(element.CreateFormServiceType);
      var match = agentForms.FirstOrDefault(f => string.Equals(f.ServicePath, path, StringComparison.OrdinalIgnoreCase));
      if (match != null)
      {
        return match;
      }
    }

    var gridType = element.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(referenceService);
    if (gridType != null && typeof(IGridService).IsAssignableFrom(gridType))
    {
      try
      {
        using var scope = AgentModuleScope.OpenForService(rootServices, gridType, fallbackDb, fallbackApp);
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, scope.Db, scope.App) is IGridService refGrid &&
            refGrid.Feature.AllowsAgentWrite &&
            CanEditFeature(refGrid.Feature, scope.App))
        {
          var gridCreate = refGrid.GetAgentForms().FirstOrDefault();
          if (gridCreate != null)
          {
            return agentForms.FirstOrDefault(f => string.Equals(f.ServicePath, gridCreate.ServicePath, StringComparison.OrdinalIgnoreCase))
              ?? gridCreate;
          }
        }
      }
      catch { }
    }

    return null;
  }

  internal static IReadOnlyList<AgentEditableChildDescriptor> ResolveEditableChildGrids(
    IReadOnlyList<ChildGridBuilder> children,
    Feature parentFeature,
    IServiceProvider rootServices,
    PlatformDbContext? fallbackDb = null,
    PlatformAppService? fallbackApp = null)
  {
    var list = new List<AgentEditableChildDescriptor>();
    foreach (var c in children)
    {
      if (c.IsFiles || c.IsReadOnly || c.ServiceType == null || !typeof(IGridService).IsAssignableFrom(c.ServiceType))
      {
        continue;
      }

      try
      {
        using var scope = AgentModuleScope.OpenForService(rootServices, c.ServiceType, fallbackDb, fallbackApp);
        if (ModuleCatalog.Instance.CreateServiceInstance(c.ServiceType, scope.Db, scope.App) is not IGridService childGrid ||
            childGrid.Feature.IsReadOnly ||
            !IsChildAgentWriteAllowed(childGrid.Feature, parentFeature) ||
            !CanViewFeature(childGrid.Feature, scope.App) ||
            !CanEditFeature(childGrid.Feature, scope.App))
        {
          continue;
        }

        var childCreate = childGrid.GetAgentForms().FirstOrDefault();
        if (childCreate == null)
        {
          continue;
        }

        using var formScope = AgentModuleScope.OpenForService(rootServices, childCreate.FormServiceType, scope.Db, scope.App);
        if (ModuleCatalog.Instance.CreateServiceInstance(childCreate.FormServiceType, formScope.Db, formScope.App) is IFormService childForm &&
            !childForm.Feature.IsReadOnly &&
            IsChildAgentWriteAllowed(childForm.Feature, parentFeature) &&
            CanEditFeature(childForm.Feature, formScope.App))
        {
          var childKey = !string.IsNullOrWhiteSpace(c.DeclaredKey) ? c.DeclaredKey : c.ServicePath;
          var title = !string.IsNullOrWhiteSpace(c.DeclaredLabel)
            ? Localizer.Instance.For(parentFeature.Module)[c.DeclaredLabel]
            : !string.IsNullOrWhiteSpace(childCreate.Label)
              ? childCreate.Label
              : Localizer.Instance.For(childGrid.Feature.Module)[childGrid.Feature.Name];
          list.Add(new AgentEditableChildDescriptor(
            c,
            childKey,
            c.ServicePath,
            childCreate.ServicePath,
            c.ServiceType,
            childCreate.FormServiceType,
            title));
        }
      }
      catch { }
    }

    return list;
  }

  internal static bool IsChildAgentReadAllowed(Feature childFeature, Feature parentFeature) =>
    ReferenceEquals(childFeature, parentFeature) ? parentFeature.AllowsAgent : childFeature.AllowsAgent;

  internal static bool IsChildAgentWriteAllowed(Feature childFeature, Feature parentFeature) =>
    ReferenceEquals(childFeature, parentFeature) ? parentFeature.AllowsAgentWrite : childFeature.AllowsAgentWrite;
}
