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

  public static IReadOnlyList<GridCreateDescriptor> DiscoverForms(PlatformDbContext db, PlatformAppService app)
  {
    var list = new List<GridCreateDescriptor>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgentWrite || !CanEditFeature(feat, app) || feat.ServiceType == null)
      {
        continue;
      }

      try
      {
        var inst = ModuleCatalog.Instance.CreateServiceInstance(feat.ServiceType, db, app);
        if (inst is IGridService grid)
        {
          foreach (var desc in grid.GetAgentForms())
          {
            if (ModuleCatalog.Instance.CreateServiceInstance(desc.FormServiceType, db, app) is IFormService descForm &&
                (!descForm.Feature.AllowsAgentWrite || !CanEditFeature(descForm.Feature, app)))
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
    PlatformAppService? app = null)
  {
    if (string.IsNullOrWhiteSpace(query))
    {
      return null;
    }

    var q = query.Trim();
    var normQ = NormalizeServiceKey(q);

    var match = descriptors.FirstOrDefault(d => string.Equals(d.ServicePath, q, StringComparison.OrdinalIgnoreCase))
      ?? descriptors.FirstOrDefault(d => string.Equals(NormalizeServiceKey(d.ServicePath), normQ, StringComparison.OrdinalIgnoreCase))
      ?? descriptors.FirstOrDefault(d => string.Equals(d.Label, q, StringComparison.OrdinalIgnoreCase))
      ?? descriptors.FirstOrDefault(d => d.FormServiceType.Name.Contains(q.Replace("-", "").Replace("/", ""), StringComparison.OrdinalIgnoreCase))
      ?? descriptors.FirstOrDefault(d => NormalizeServiceKey(d.ServicePath).Contains(normQ, StringComparison.OrdinalIgnoreCase));

    if (match != null || db == null || app == null)
    {
      return match;
    }

    var directType = ModuleCatalog.Instance.FindServiceType(q);
    if (directType != null)
    {
      try
      {
        if (typeof(IGridService).IsAssignableFrom(directType) &&
            ModuleCatalog.Instance.CreateServiceInstance(directType, db, app) is IGridService grid &&
            grid.Feature.AllowsAgentWrite &&
            CanEditFeature(grid.Feature, app))
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
            ModuleCatalog.Instance.CreateServiceInstance(directType, db, app) is IFormService form &&
            form.Feature.AllowsAgentWrite &&
            CanEditFeature(form.Feature, app))
        {
          if (form.Feature.ServiceType != null &&
              typeof(IGridService).IsAssignableFrom(form.Feature.ServiceType) &&
              ModuleCatalog.Instance.CreateServiceInstance(form.Feature.ServiceType, db, app) is IGridService ownerGrid)
          {
            foreach (var create in ownerGrid.GetAgentForms())
            {
              var found = descriptors.FirstOrDefault(d => string.Equals(d.ServicePath, create.ServicePath, StringComparison.OrdinalIgnoreCase));
              if (found != null)
              {
                return found;
              }
            }
          }

          return new GridCreateDescriptor(directType, form.Service, form.GetTitle(), UiColor.Blue);
        }
      }
      catch { }
    }

    return null;
  }

  public static async Task<GridCreateDescriptor> ResolveEditFormDescriptorAsync(
    IReadOnlyList<GridCreateDescriptor> descriptors,
    GridCreateDescriptor fallbackDesc,
    string? requestedService,
    long recordId,
    PlatformDbContext db,
    PlatformAppService app,
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
        else if (typeof(IFormService).IsAssignableFrom(directType))
        {
          try
          {
            if (ModuleCatalog.Instance.CreateServiceInstance(directType, db, app) is IFormService reqForm &&
                reqForm.Feature.ServiceType != null &&
                typeof(IGridService).IsAssignableFrom(reqForm.Feature.ServiceType))
            {
              gridServiceType = reqForm.Feature.ServiceType;
            }
          }
          catch { }
        }
      }
    }

    if (gridServiceType == null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(fallbackDesc.FormServiceType, db, app) is IFormService formInst &&
            formInst.Feature.ServiceType != null &&
            typeof(IGridService).IsAssignableFrom(formInst.Feature.ServiceType))
        {
          gridServiceType = formInst.Feature.ServiceType;
        }
      }
      catch { }
    }

    if (gridServiceType != null)
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(gridServiceType, db, app) is IGridService ownerGrid &&
            CanViewFeature(ownerGrid.Feature, app))
        {
          var rowObj = await ownerGrid.ReadRowAsync(recordId, ct);
          if (rowObj?.Open?.Service is { Length: > 0 } rowFormPath &&
              !string.IsNullOrWhiteSpace(rowFormPath))
          {
            var matched = ResolveFormDescriptor(descriptors, rowFormPath, db, app);
            if (matched != null)
            {
              return matched;
            }
          }

          var creates = ownerGrid.GetAgentForms();
          if (creates.Count == 1)
          {
            var singleMatch = ResolveFormDescriptor(descriptors, creates[0].ServicePath, db, app);
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
    PlatformDbContext db,
    PlatformAppService app)
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
        if (ModuleCatalog.Instance.CreateServiceInstance(gridType, db, app) is IGridService refGrid &&
            refGrid.Feature.AllowsAgentWrite &&
            CanEditFeature(refGrid.Feature, app))
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

  public static GridCreateDescriptor? ResolveChildCreateForm(
    Type childGridServiceType,
    PlatformDbContext db,
    PlatformAppService app)
  {
    try
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(childGridServiceType, db, app) is IGridService childGrid &&
          !childGrid.Feature.IsReadOnly &&
          CanViewFeature(childGrid.Feature, app) &&
          CanEditFeature(childGrid.Feature, app))
      {
        var createDesc = childGrid.GetCreateForms().FirstOrDefault();
        if (createDesc != null &&
            ModuleCatalog.Instance.CreateServiceInstance(createDesc.FormServiceType, db, app) is IFormService childForm &&
            !childForm.Feature.IsReadOnly &&
            CanEditFeature(childForm.Feature, app))
        {
          return createDesc;
        }
      }
    }
    catch { }

    return null;
  }

  public static (ChildGridBuilder Child, IGridService ChildGrid, GridCreateDescriptor ChildCreate, IFormService ChildForm)? ResolveEditableChildLineGrid(
    IReadOnlyList<ChildGridBuilder> children,
    PlatformDbContext db,
    PlatformAppService app)
  {
    foreach (var c in children)
    {
      if (c.IsFiles || c.IsReadOnly || c.ServiceType == null || !typeof(IGridService).IsAssignableFrom(c.ServiceType))
      {
        continue;
      }

      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(c.ServiceType, db, app) is not IGridService childGrid ||
            childGrid.Feature.IsReadOnly ||
            !CanViewFeature(childGrid.Feature, app) ||
            !CanEditFeature(childGrid.Feature, app))
        {
          continue;
        }

        var childCreate = childGrid.GetCreateForms().FirstOrDefault();
        if (childCreate != null &&
            ModuleCatalog.Instance.CreateServiceInstance(childCreate.FormServiceType, db, app) is IFormService childForm &&
            !childForm.Feature.IsReadOnly &&
            CanEditFeature(childForm.Feature, app))
        {
          return (c, childGrid, childCreate, childForm);
        }
      }
      catch { }
    }

    return null;
  }
}
