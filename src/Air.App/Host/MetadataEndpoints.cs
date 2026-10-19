using Air.App.Auth;
using Air.App.Localization;
using Air.App.Metadata;
using Air.App.Modules;
using Air.Data;
using Air.Data.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Air.App.Host;

internal static class MetadataEndpoints
{
  public static void MapMetadataEndpoints(this RouteGroupBuilder api)
  {
    var getMetadataHandler = async (
      ModuleCatalog catalog,
      HttpContext httpContext,
      IUserContext userContext,
      IAuthStore authStore,
      CancellationToken ct) =>
      await PlatformEndpoints.WithRequestCultureAsync(httpContext.Request, async culture =>
      {
        Localizer.Instance.EnsureManagersLoaded();
        var texts = UiTexts.English.Keys.ToDictionary(k => k, k => Localizer.Instance[k]);
        var isUk = culture.TwoLetterISOLanguageName.Equals("uk", StringComparison.OrdinalIgnoreCase);

        var tenants = await authStore.GetActiveTenantsAsync(ct);
        if (userContext.IsAuthenticated && userContext.UserId is { } uid)
        {
          var user = await authStore.FindUserByIdAsync(uid, ct);
          if (user is { IsRoot: false })
          {
            var sec = await authStore.LoadSecurityInfoAsync(uid, ct);
            var homeTid = (user as Air.Data.Entities.IHasTenant)?.TenantId;
            if (!sec.Global && !sec.Permissions.Contains("*", StringComparer.OrdinalIgnoreCase))
            {
              tenants = tenants.Where(t => sec.CanAccessTenant(t.Id) || t.Id == homeTid).ToList();
            }
          }
        }
        var activeTenantId = AsyncLocalTenantContext.Instance.TenantId
          ?? (userContext.IsAuthenticated && userContext.Claim(AuthClaims.Tenant) is { } tc && long.TryParse(tc, out var tId) ? tId : 1);
        var currentTenant = tenants.FirstOrDefault(t => t.Id == activeTenantId) ?? tenants.FirstOrDefault();

        var metadata = new AppMetadataDto
        {
          Texts = texts,
          Language = new SwitcherDto
          {
            Label = Localizer.Instance["Language"],
            Current = isUk
              ? new Reference { Id = 2, Name = Localizer.Instance["Ukrainian"] }
              : new Reference { Id = 1, Name = Localizer.Instance["English"] },
            Options =
            [
              new Reference { Id = 1, Name = Localizer.Instance["English"] },
              new Reference { Id = 2, Name = Localizer.Instance["Ukrainian"] }
            ],
            Service = "platform-language",
            Method = "switch"
          },
          Tenant = currentTenant != null ? new SwitcherDto
          {
            Label = Localizer.Instance["Tenant"],
            Current = new Reference { Id = currentTenant.Id, Name = currentTenant.Name },
            Options = tenants.Count > 1 ? tenants.Select(t => new Reference { Id = t.Id, Name = t.Name }).ToList() : null,
            Service = tenants.Count > 1 ? "platform-tenant" : null,
            Method = tenants.Count > 1 ? "switch" : null
          } : null,
          DefaultCurrency = "UAH"
        };

        foreach (var module in catalog.Modules)
        {
          var visibleNodes = module.Menu.Nodes
            .Select(n => ToMenuItem(module.Handle, n, userContext))
            .Where(n => n != null)
            .Select(n => n!)
            .ToList();

          var trimmedNodes = TrimSeparators(visibleNodes);
          if (trimmedNodes.Count == 0) continue;

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

        foreach (var feat in catalog.AllFeatures)
        {
          if (userContext.IsAuthenticated && !userContext.Can(feat.View))
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
              Handle = feat.Name.ToLowerInvariant(),
              Name = featL[feat.Name],
              Screen = screen
            });
          }

          var flatPath = feat.Path.Replace("-", "", StringComparison.Ordinal);
          if (!string.Equals(flatPath, feat.Path, StringComparison.OrdinalIgnoreCase) && metadata.Routes.All(r => r.Path != flatPath))
          {
            metadata.Routes.Add(new MenuItemDto
            {
              Path = flatPath,
              Handle = feat.Name.ToLowerInvariant(),
              Name = featL[feat.Name],
              Screen = screen
            });
          }
        }

        return Results.Ok(metadata);
      });

    api.MapGet("/metadata", getMetadataHandler);
    api.MapPost("/metadata/get-data", getMetadataHandler);
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
      if (node.IsHidden) return null;
      if (user.IsAuthenticated && (!user.Can(feature.View) || !user.Can(feature.MenuView)))
      {
        return null;
      }

      var modLower = moduleHandle.ToLowerInvariant();
      var fullPath = feature.Path;
      if (fullPath is not null && !fullPath.StartsWith(modLower + "/", StringComparison.OrdinalIgnoreCase))
      {
        fullPath = $"{modLower}/{fullPath.TrimStart('/')}";
      }

      return new MenuItemDto
      {
        Path = fullPath,
        Handle = node.Handle,
        Name = node.Title != null ? modL[node.Title] : null,
        Icon = node.Icon,
        Screen = feature.ServiceType != null ? ServicePathResolver.Resolve(feature.ServiceType) : feature.Path
      };
    }

    var visibleChildren = node.Children
      .Select(c => ToMenuItem(moduleHandle, c, user))
      .Where(c => c != null)
      .Select(c => c!)
      .ToList();

    var trimmedChildren = TrimSeparators(visibleChildren);
    if (trimmedChildren.Count == 0) return null;

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
