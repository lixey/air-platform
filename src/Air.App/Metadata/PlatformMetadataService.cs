namespace Platform.App.Metadata;

/// <summary>
/// Platform application metadata service mounted at <c>POST /api/metadata/get-data</c>.
/// </summary>
[ServiceRoute("metadata")]
public sealed class PlatformMetadataService(PlatformAppService? app = null) : PlatformService(app)
{
  private static readonly Feature MetadataFeature = new("Platform", "Metadata", typeof(PlatformMetadataService));

  public override Feature Feature => MetadataFeature;

  [Endpoint(Auth = EndpointAuth.Custom, ResponseType = typeof(AppMetadataDto))]
  public async Task<IResult> GetData(
    HttpContext httpContext,
    IAuthStore authStore,
    PlatformOptions? platformOptions = null,
    CancellationToken ct = default)
  {
    if (User is not { IsAuthenticated: true } userContext)
    {
      return Results.Unauthorized();
    }

    Localizer.Instance.EnsureManagersLoaded();
    var texts = UiTexts.English.Keys.ToDictionary(k => k, k => Localizer.Instance[k]);
    var activeCulture = Cultures.Resolve(CultureInfo.CurrentUICulture.Name);

    var tenants = await authStore.GetActiveTenantsAsync(ct);
    if (httpContext.Items[AuthClaims.SnapshotItem] is UserAuthSnapshot snapshot && snapshot.IsActive)
    {
      if (!snapshot.IsRoot)
      {
        var sec = snapshot.ToSecurityInfo();
        if (!sec.Global && !snapshot.PermissionSet.Contains("*"))
        {
          tenants = [.. tenants.Where(t => sec.CanAccessTenant(t.Id) || t.Id == snapshot.HomeTenantId)];
        }
      }
    }
    else if (userContext.UserId is { } uid)
    {
      var user = await authStore.FindUserByIdAsync(uid, ct);
      if (user is { IsRoot: false })
      {
        var sec = await authStore.LoadSecurityInfoAsync(uid, ct);
        var homeTid = (user as IHasTenant)?.TenantId;
        if (!sec.Global && !sec.Permissions.Contains("*", StringComparer.OrdinalIgnoreCase))
        {
          tenants = [.. tenants.Where(t => sec.CanAccessTenant(t.Id) || t.Id == homeTid)];
        }
      }
    }

    var activeTenantId = AsyncLocalTenantContext.Instance.TenantId
      ?? (userContext.Claim(AuthClaims.Tenant) is { } tc && long.TryParse(tc, out var tId) ? tId : 1);
    var currentTenant = tenants.FirstOrDefault(t => t.Id == activeTenantId) ?? tenants.FirstOrDefault();
    var hasTenantMenu = Catalog.Modules
      .SelectMany(m => ModuleCatalog.Flatten(m.Menu.Nodes))
      .Any(n => !n.IsHidden && n.Feature?.Handle.Equals("Tenant", StringComparison.OrdinalIgnoreCase) == true);
    var showTenantSwitcher = currentTenant != null && (tenants.Count > 1 || hasTenantMenu);

    var metadata = new AppMetadataDto
    {
      Texts = texts,
      Language = new SwitcherDto
      {
        Label = Localizer.Instance["Language"],
        Current = new Reference { Id = activeCulture.Id, Name = activeCulture.Handle },
        Options = [.. Cultures.Cultures.Select(c => new Reference { Id = c.Id, Name = c.DisplayName })],
        Service = "platform-language",
        Method = "switch"
      },
      Tenant = showTenantSwitcher ? new SwitcherDto
      {
        Label = Localizer.Instance["Tenant"],
        Current = new Reference { Id = currentTenant!.Id, Name = currentTenant.Name },
        Options = tenants.Count > 1 ? [.. tenants.Select(t => new Reference { Id = t.Id, Name = t.Name })] : null,
        Service = tenants.Count > 1 ? "platform-tenant" : null,
        Method = tenants.Count > 1 ? "switch" : null
      } : null,
      DefaultCurrency = (platformOptions ?? new PlatformOptions()).DefaultCurrency
    };

    foreach (var module in Catalog.Modules)
    {
      var visibleNodes = module.Menu.Nodes
        .Select(n => ToMenuItem(module.Handle, n, userContext))
        .OfType<MenuItemDto>()
        .ToList();

      var trimmedNodes = TrimSeparators(visibleNodes);
      if (trimmedNodes.Count == 0)
      {
        continue;
      }

      var rootItem = new MenuItemDto
      {
        Handle = module.Handle,
        Name = Localizer.Instance.For(module.Handle)[module.Name],
        Path = module.Handle.ToLowerInvariant(),
        Items = trimmedNodes
      };
      metadata.Menu.Add(rootItem);
      metadata.Routes.AddRange(Flatten(rootItem));
    }

    foreach (var feat in Catalog.AllFeatures)
    {
      if (!ServiceEndpointsExtensions.CanView(feat, userContext))
      {
        continue;
      }

      var featL = Localizer.Instance.For(feat.Module);
      var screen = feat.ServiceType != null ? ServicePathResolver.Resolve(feat.ServiceType) : feat.Path;
      if (metadata.Routes.All(r => r.Screen != screen && r.Path != feat.Path))
      {
        metadata.Routes.Add(new MenuItemDto
        {
          Path = feat.Path,
          Handle = feat.Handle,
          Name = featL[feat.Name],
          Title = feat.Title != null ? featL[feat.Title] : null,
          Screen = screen
        });
      }
    }

    return Results.Ok(metadata);
  }

  private static MenuItemDto? ToMenuItem(string moduleHandle, ModuleMenuNode node, IUserContext user)
  {
    var modL = Localizer.Instance.For(moduleHandle);
    if (node.IsSeparator)
    {
      return new MenuItemDto { IsSeparator = true };
    }

    if (node.Feature is { } feature)
    {
      if (node.IsHidden || !ServiceEndpointsExtensions.CanView(feature, user) || !user.Can(feature.MenuView))
      {
        return null;
      }

      var modLower = moduleHandle.ToLowerInvariant();
      var fullPath = feature.Path;
      if (fullPath is not null && !fullPath.StartsWith(modLower + "/", StringComparison.OrdinalIgnoreCase))
      {
        fullPath = $"{modLower}/{fullPath.TrimStart('/')}";
      }

      var tooltip = node.Tooltip ?? feature.Title;
      return new MenuItemDto
      {
        Path = fullPath,
        Handle = node.Handle,
        Name = node.Title != null ? modL[node.Title] : null,
        Title = tooltip != null ? modL[tooltip] : null,
        Icon = node.Icon,
        Screen = feature.ServiceType != null ? ServicePathResolver.Resolve(feature.ServiceType) : feature.Path
      };
    }

    var visibleChildren = node.Children
      .Select(c => ToMenuItem(moduleHandle, c, user))
      .OfType<MenuItemDto>()
      .ToList();

    var trimmedChildren = TrimSeparators(visibleChildren);
    if (trimmedChildren.Count == 0)
    {
      return null;
    }

    return new MenuItemDto
    {
      Handle = node.Handle,
      Name = node.Title != null ? modL[node.Title] : null,
      Icon = node.Icon,
      Items = trimmedChildren
    };
  }

  private static List<MenuItemDto> TrimSeparators(List<MenuItemDto> items)
  {
    var trimmed = new List<MenuItemDto>();
    foreach (var item in items)
    {
      if (item.IsSeparator && (trimmed.Count == 0 || trimmed[^1].IsSeparator))
      {
        continue;
      }

      trimmed.Add(item);
    }

    if (trimmed.Count > 0 && trimmed[^1].IsSeparator)
    {
      trimmed.RemoveAt(trimmed.Count - 1);
    }

    return trimmed;
  }

  private static IEnumerable<MenuItemDto> Flatten(MenuItemDto item)
  {
    if (item.Items.Count == 0 && item.Path != null)
    {
      yield return item;
    }
    else
    {
      foreach (var child in item.Items)
      {
        foreach (var leaf in Flatten(child))
        {
          yield return leaf;
        }
      }
    }
  }
}
