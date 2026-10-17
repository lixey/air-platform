using System.Text.Json;
using Air.App.Auth;
using Air.App.Grid;
using Air.App.Form;
using Air.App.Localization;
using Air.App.Metadata;
using Air.App.Modules;
using Air.App.Screens;
using Air.Data.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Host;

public static class PlatformEndpoints
{
  public static void MapPlatformEndpoints(this WebApplication app)
  {
    var api = app.MapGroup("/api");

    // 1. Metadata (both GET and POST for convenience)
    var getMetadataHandler = (ModuleCatalog catalog) =>
    {
      var metadata = new AppMetadataDto
      {
        Texts = UiTexts.English.ToDictionary()
      };

      foreach (var module in catalog.Modules)
      {
        var rootItem = new MenuItemDto
        {
          Handle = module.Handle,
          Name = module.Name,
          Path = module.Handle,
          Items = module.Menu.Nodes.Select(n => ToMenuItem(module.Handle, n)).ToList()
        };
        metadata.Menu.Add(rootItem);
        metadata.Routes.AddRange(Flatten(rootItem));
      }

      foreach (var feat in catalog.AllFeatures)
      {
        if (metadata.Routes.All(r => r.Screen != feat.Path))
        {
          metadata.Routes.Add(new MenuItemDto
          {
            Path = feat.Path,
            Handle = feat.Name.ToLowerInvariant(),
            Name = feat.Name,
            Screen = feat.Path
          });
        }
      }

      return Results.Ok(metadata);
    };

    api.MapGet("/metadata", getMetadataHandler);
    api.MapPost("/metadata/get-data", getMetadataHandler);

    // 2. Auth Endpoints
    api.MapPost("/auth/get-profile", () =>
    {
      var user = new UserProfileDto
      {
        Id = 1,
        Login = "admin",
        Name = "Demo Administrator",
        Email = "admin@air.platform",
        Language = "en",
        TimeZone = "UTC",
        Permissions = ["*"]
      };
      return Results.Ok(user);
    });

    api.MapPost("/auth/login", (LoginRequest? req) =>
    {
      var login = req?.Login ?? "admin";
      var session = new SessionDto
      {
        User = new UserProfileDto
        {
          Id = 1,
          Login = login,
          Name = "Demo Administrator",
          Email = $"{login}@air.platform",
          Language = "en",
          TimeZone = "UTC",
          Permissions = ["*"]
        },
        ExpiresIn = 86400
      };
      return Results.Ok(session);
    });

    api.MapPost("/auth/refresh", () =>
    {
      var session = new SessionDto
      {
        User = new UserProfileDto
        {
          Id = 1,
          Login = "admin",
          Name = "Demo Administrator",
          Language = "en",
          TimeZone = "UTC",
          Permissions = ["*"]
        },
        ExpiresIn = 86400
      };
      return Results.Ok(session);
    });

    api.MapPost("/auth/logout", () => Results.Ok(new { }));

    // 3. Dynamic service endpoints
    api.MapPost("/{**servicePath}", async (
      string servicePath,
      HttpRequest httpRequest,
      ModuleCatalog catalog,
      IServiceProvider services,
      CancellationToken ct) =>
    {
      var parts = servicePath.Trim('/').Split('/');
      if (parts.Length < 2)
      {
        return Results.NotFound();
      }

      var method = parts[^1];
      var serviceName = string.Join('/', parts[..^1]);

      // Find feature by path or service name
      var feat = catalog.FindFeature(serviceName)
        ?? catalog.FindFeature(serviceName.Replace("demo/", ""))
        ?? catalog.FindFeature($"demo/{serviceName}")
        ?? catalog.FindFeature(parts[0] + "/" + parts[1]);

      if (feat?.ServiceType is null)
      {
        return Results.NotFound($"Service '{serviceName}' not found.");
      }

      if (typeof(IGridService).IsAssignableFrom(feat.ServiceType))
      {
        var grid = (IGridService)services.GetRequiredService(feat.ServiceType);

        if (method == "get-data")
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          var screen = await grid.BuildScreenAsync(serviceName, ct);
          var page = await grid.GetDataAsync(req, ct);
          screen.Data = page;
          return Results.Ok(screen);
        }

        if (method == "get-count")
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          var page = await grid.GetDataAsync(req, ct);
          return Results.Ok(new GridCountDto { Total = page.Total ?? 0 });
        }

        if (method == "delete")
        {
          var ids = await ExtractIdsAsync(httpRequest, ct);
          int deleted = 0;
          foreach (var id in ids)
          {
            await grid.DeleteAsync(id, ct);
            deleted++;
          }
          return Results.Ok(new GridDeleteDto { Deleted = deleted, Outcome = MutationOutcome.Committed });
        }

        // Custom action dispatching on grid
        var actionMethod = feat.ServiceType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
          .FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(m.Name, method.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
        if (actionMethod != null)
        {
          GridRequest? req = null;
          if (httpRequest.ContentLength > 0)
          {
            try { req = await httpRequest.ReadFromJsonAsync<GridRequest>(cancellationToken: ct); } catch { }
          }
          req ??= new GridRequest();
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var idVal)) req.Id = idVal;
          if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
          if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

          var parameters = actionMethod.GetParameters();
          object?[] args = new object?[parameters.Length];
          for (int i = 0; i < parameters.Length; i++)
          {
            var pType = parameters[i].ParameterType;
            if (pType == typeof(CancellationToken)) args[i] = ct;
            else if (typeof(GridRequest).IsAssignableFrom(pType)) args[i] = req;
            else if (pType == typeof(long) && req.Id.HasValue) args[i] = req.Id.Value;
            else args[i] = null;
          }

          var resultTask = actionMethod.Invoke(grid, args);
          if (resultTask is Task task)
          {
            await task.ConfigureAwait(false);
            var resultProp = task.GetType().GetProperty("Result");
            var result = resultProp?.GetValue(task);
            return Results.Ok(result ?? new GridActionDto { Outcome = MutationOutcome.Committed });
          }
          return Results.Ok(resultTask ?? new GridActionDto { Outcome = MutationOutcome.Committed });
        }
      }
      else if (typeof(IFormService).IsAssignableFrom(feat.ServiceType))
      {
        var form = (IFormService)services.GetRequiredService(feat.ServiceType);

        if (method == "get-data")
        {
          long? id = null;
          if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId))
          {
            id = parsedId;
          }
          else if (httpRequest.ContentLength > 0)
          {
            try
            {
              using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
              if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var bodyId))
              {
                id = bodyId;
              }
            }
            catch { }
          }

          var screen = await form.BuildScreenAsync(serviceName, id, ct);
          var entity = await form.GetModelAsync(id, ct);
          var entityId = (entity as Air.Data.Entities.IEntity)?.Id;
          var ver = (entity as Air.Data.Entities.IHasVersion)?.Ver ?? 1;

          var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
          var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];

          screen.Data = new FormDataDto
          {
            Id = (entityId > 0) ? entityId : null,
            Ver = (entityId > 0) ? ver : null,
            Value = dict
          };

          return Results.Ok(screen);
        }

        if (method == "save")
        {
          using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
          var entity = await form.SaveAsync(doc.RootElement, ct);
          var entityId = (entity as Air.Data.Entities.IEntity)?.Id;
          var ver = (entity as Air.Data.Entities.IHasVersion)?.Ver;

          var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
          var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];

          return Results.Ok(new FormDataDto
          {
            Id = entityId,
            Ver = ver,
            Value = dict,
            Outcome = MutationOutcome.Committed
          });
        }

        if (method == "delete")
        {
          var ids = await ExtractIdsAsync(httpRequest, ct);
          int deleted = 0;
          foreach (var id in ids)
          {
            await form.DeleteAsync(id, ct);
            deleted++;
          }
          return Results.Ok(new GridDeleteDto { Deleted = deleted, Outcome = MutationOutcome.Committed });
        }
      }

      return Results.NotFound();
    });
  }

  private static async Task<List<long>> ExtractIdsAsync(HttpRequest request, CancellationToken ct)
  {
    List<long> ids = [];
    if (request.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId))
    {
      ids.Add(parsedId);
      return ids;
    }

    if (request.ContentLength > 0)
    {
      try
      {
        using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        if (doc.RootElement.TryGetProperty("selectedIds", out var sel) && sel.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in sel.EnumerateArray())
          {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id)) ids.Add(id);
            else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var sid)) ids.Add(sid);
          }
        }
        else if (doc.RootElement.TryGetProperty("id", out var idProp))
        {
          if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var id)) ids.Add(id);
          else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var sid)) ids.Add(sid);
        }
      }
      catch { }
    }

    return ids;
  }

  private static MenuItemDto ToMenuItem(string moduleHandle, ModuleMenuNode node)
  {
    var fullPath = node.Feature?.Path;
    if (fullPath is not null && !fullPath.StartsWith(moduleHandle + "/", StringComparison.OrdinalIgnoreCase))
    {
      fullPath = $"{moduleHandle}/{fullPath}";
    }

    return new MenuItemDto
    {
      Path = fullPath,
      Handle = node.Handle,
      Name = node.Title,
      Icon = node.Icon,
      Screen = node.Feature?.Path,
      IsSeparator = node.IsSeparator,
      Items = node.Children.Where(c => !c.IsHidden).Select(c => ToMenuItem(moduleHandle, c)).ToList()
    };
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
