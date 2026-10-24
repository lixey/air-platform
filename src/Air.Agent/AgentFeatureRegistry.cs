namespace Platform.Agent;

public static class AgentFeatureRegistry
{
  public static bool CanViewFeature(Feature? feat, PlatformAppService app)
  {
    if (feat == null || string.IsNullOrEmpty(feat.Module) || app.User is not { IsAuthenticated: true } user)
    {
      return true;
    }

    return user.Can(feat.View) ||
           user.Can(feat.MenuView) ||
           user.Can(feat.Edit) ||
           user.Can(feat.All) ||
           user.Can("*");
  }

  public static bool CanEditFeature(Feature? feat, PlatformAppService app)
  {
    if (feat == null || feat.IsReadOnly)
    {
      return false;
    }

    if (string.IsNullOrEmpty(feat.Module) || app.User is not { IsAuthenticated: true } user)
    {
      return true;
    }

    return user.Can(feat.Edit) ||
           user.Can(feat.All) ||
           user.Can("*");
  }

  public static bool IsEditableElement(FormElementDto el) =>
    !el.IsDisabled && !el.IsServerOwned && !el.IsHidden && el.Model is not FormInfoModel;

  public static IReadOnlyList<GridCreateDescriptor> DiscoverForms(PlatformDbContext db, PlatformAppService app)
  {
    var list = new List<GridCreateDescriptor>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var feat in ModuleCatalog.Instance.AllFeatures)
    {
      if (!feat.AllowsAgent || !CanEditFeature(feat, app) || feat.ServiceType == null)
      {
        continue;
      }

      try
      {
        var inst = ModuleCatalog.Instance.CreateServiceInstance(feat.ServiceType, db, app);
        if (inst is IGridService grid)
        {
          foreach (var desc in grid.GetCreateForms())
          {
            if (seen.Add(desc.ServicePath))
            {
              list.Add(desc);
            }
          }
        }
        else if (inst is IFormService form)
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
            CanEditFeature(grid.Feature, app))
        {
          foreach (var create in grid.GetCreateForms())
          {
            var found = descriptors.FirstOrDefault(d => string.Equals(d.ServicePath, create.ServicePath, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
              return found;
            }
          }

          return grid.GetCreateForms().FirstOrDefault();
        }

        if (typeof(IFormService).IsAssignableFrom(directType) &&
            ModuleCatalog.Instance.CreateServiceInstance(directType, db, app) is IFormService form &&
            CanEditFeature(form.Feature, app))
        {
          if (form.Feature.ServiceType != null &&
              typeof(IGridService).IsAssignableFrom(form.Feature.ServiceType) &&
              ModuleCatalog.Instance.CreateServiceInstance(form.Feature.ServiceType, db, app) is IGridService ownerGrid)
          {
            foreach (var create in ownerGrid.GetCreateForms())
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
          if (rowObj is IDictionary<string, object?> rowDict &&
              rowDict.TryGetValue("form", out var formVal) &&
              formVal is string rowFormPath &&
              !string.IsNullOrWhiteSpace(rowFormPath))
          {
            var matched = ResolveFormDescriptor(descriptors, rowFormPath, db, app);
            if (matched != null)
            {
              return matched;
            }
          }

          var creates = ownerGrid.GetCreateForms();
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
            (refGrid.Feature.AllowsAgent || element.AllowsCreate) &&
            CanEditFeature(refGrid.Feature, app))
        {
          var gridCreate = refGrid.GetCreateForms().FirstOrDefault();
          if (gridCreate != null)
          {
            return agentForms.FirstOrDefault(f => string.Equals(f.ServicePath, gridCreate.ServicePath, StringComparison.OrdinalIgnoreCase))
              ?? gridCreate;
          }
        }
      }
      catch { }
    }

    var prefix = referenceService.EndsWith("-grid", StringComparison.OrdinalIgnoreCase)
      ? referenceService[..^5]
      : referenceService;
    var candidateFormPath = $"{prefix}-form";
    return agentForms.FirstOrDefault(f => string.Equals(f.ServicePath, candidateFormPath, StringComparison.OrdinalIgnoreCase));
  }
}
