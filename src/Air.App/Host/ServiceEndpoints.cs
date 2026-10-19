using System.Reflection;
using System.Text.Json;
using Air.App.Auth;
using Air.App.Form;
using Air.App.Grid;
using Air.App.Localization;
using Air.App.Modules;
using Air.App.Screens;
using Air.Data.Contracts;
using Air.Data.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Air.App.Host;

internal static class ServiceEndpoints
{
  public static void MapServiceEndpoints(this RouteGroupBuilder api)
  {
    api.MapPost("/{**servicePath}", async (
      string servicePath,
      HttpRequest httpRequest,
      ModuleCatalog catalog,
      IServiceProvider services,
      CancellationToken ct) =>
      await PlatformEndpoints.WithRequestCultureAsync(httpRequest, async _ =>
      {
        Localizer.Instance.EnsureManagersLoaded();
        var parts = servicePath.Trim('/').Split('/');
        if (parts.Length < 2)
        {
          return Results.NotFound();
        }

        string serviceName;
        string method;
        string? subMethod = null;

        if (parts.Length >= 3 && string.Equals(parts[^2], "move-status", StringComparison.OrdinalIgnoreCase))
        {
          serviceName = string.Join('/', parts[..^2]);
          method = "move-status";
          subMethod = parts[^1];
        }
        else
        {
          serviceName = string.Join('/', parts[..^1]);
          method = parts[^1];
        }

        var feat = catalog.FindFeature(serviceName)
          ?? (parts.Length >= 2 ? catalog.FindFeature(parts[0] + "/" + parts[1]) : null);

        var serviceType = feat?.ServiceType
          ?? catalog.FindServiceType(serviceName)
          ?? (parts.Length >= 2 ? catalog.FindServiceType(parts[0] + "/" + parts[1]) : null);

        if (serviceType is null)
        {
          return Results.NotFound($"Service '{serviceName}' not found.");
        }

        if (typeof(IGridService).IsAssignableFrom(serviceType))
        {
          return await DispatchGridAsync(serviceType, serviceName, method, subMethod, feat, httpRequest, services, ct);
        }

        if (typeof(IFormService).IsAssignableFrom(serviceType))
        {
          return await DispatchFormAsync(serviceType, serviceName, method, subMethod, httpRequest, services, ct);
        }

        if (typeof(IScreenService).IsAssignableFrom(serviceType))
        {
          var screenService = (IScreenService)services.GetRequiredService(serviceType);
          if (method == "get-data")
          {
            return Results.Ok(await screenService.BuildScreenAsync(serviceName, ct));
          }
        }

        return Results.NotFound();
      }));
  }

  private static async Task<IResult> DispatchGridAsync(
    Type serviceType,
    string serviceName,
    string method,
    string? subMethod,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var grid = (IGridService)services.GetRequiredService(serviceType);
    feat ??= grid.Feature;
    if (services.GetService<PlatformAppService>() is { } appInstance)
    {
      grid.AttachPlatformApp(appInstance);
    }

    switch (method.ToLowerInvariant())
    {
      case "get-data":
      {
        var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
        var page = await grid.GetDataAsync(req, ct);
        if (req.IsDataOnly)
        {
          return Results.Ok(new ScreenDto { Data = page });
        }

        var screen = await grid.BuildScreenAsync(serviceName, req, ct);
        screen.Data = page;
        return Results.Ok(screen);
      }

      case "get-count":
      {
        var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
        return Results.Ok(await grid.GetCountAsync(req, ct));
      }

      case "delete":
      {
        var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
        return PlatformEndpoints.ToMutationResult(await grid.DeleteAsync(req, ct));
      }

      case "save-cell":
      {
        var req = await ReadGridRequestAsync<GridCellRequest>(httpRequest, ct);
        return PlatformEndpoints.ToMutationResult(await grid.SaveCellAsync(req, services, ct));
      }

      case "move-status":
      {
        GridRequest? req = null;
        JsonElement? bodyElement = null;
        if (httpRequest.ContentLength > 0)
        {
          try
          {
            using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
            bodyElement = doc.RootElement.Clone();
            req = JsonSerializer.Deserialize<GridRequest>(bodyElement.Value.GetRawText(), PlatformJsonOptions.DefaultOptions);
          }
          catch { }
        }
        req ??= new GridRequest();
        ApplyGridQueryParams(httpRequest, req);
        return PlatformEndpoints.ToMutationResult(await grid.MoveStatusAsync(req, subMethod ?? "", bodyElement, ct));
      }

      case "export":
      {
        var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
        return await grid.ExportAsync(req, ct);
      }

      case "upload":
      {
        var uploadMethod = serviceType.GetMethod("Upload");
        if (uploadMethod != null && httpRequest.HasFormContentType)
        {
          var formCollection = await httpRequest.ReadFormAsync(ct);
          var uploadReq = new Files.Contracts.GridUploadRequest();
          if (formCollection.TryGetValue("parentService", out var psVal)) uploadReq.ParentService = psVal;
          if (formCollection.TryGetValue("parentId", out var pidVal) && long.TryParse(pidVal, out var parsedPid)) uploadReq.ParentId = parsedPid;
          if (formCollection.TryGetValue("afterId", out var aidVal) && long.TryParse(aidVal, out var parsedAid)) uploadReq.AfterId = parsedAid;
          if (formCollection.TryGetValue("metadata", out var metaVal)) uploadReq.Metadata = metaVal;
          if (httpRequest.Query.TryGetValue("parentId", out var qPid) && long.TryParse(qPid, out var parsedQPid)) uploadReq.ParentId = parsedQPid;
          if (httpRequest.Query.TryGetValue("parentService", out var qPs)) uploadReq.ParentService = qPs;

          return await InvokeServiceMethodAsync(uploadMethod, grid, [uploadReq, formCollection.Files, ct]);
        }
        break;
      }
    }

    var actionMethod = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(m.Name, method.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
    if (actionMethod == null)
    {
      return Results.NotFound();
    }

    if (!IsCustomActionPermitted(actionMethod, feat, services.GetService<IUserContext>()))
    {
      return Results.UnprocessableEntity(new
      {
        outcome = MutationOutcome.Refused,
        messages = new[] { new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error } }
      });
    }

    var parameters = actionMethod.GetParameters();
    var pickReqType = parameters.FirstOrDefault(p => typeof(GridRequest).IsAssignableFrom(p.ParameterType))?.ParameterType ?? typeof(GridRequest);
    var customReq = await ReadGridRequestAsync(httpRequest, pickReqType, ct);

    var args = new object?[parameters.Length];
    for (int i = 0; i < parameters.Length; i++)
    {
      var pType = parameters[i].ParameterType;
      if (pType == typeof(CancellationToken)) args[i] = ct;
      else if (typeof(GridRequest).IsAssignableFrom(pType)) args[i] = customReq;
      else if (pType == typeof(long) && customReq.Id.HasValue) args[i] = customReq.Id.Value;
    }

    return await InvokeServiceMethodAsync(actionMethod, grid, args);
  }

  private static async Task<IResult> DispatchFormAsync(
    Type serviceType,
    string serviceName,
    string method,
    string? subMethod,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var form = (IFormService)services.GetRequiredService(serviceType);
    if (services.GetService<PlatformAppService>() is { } appInstance)
    {
      form.AttachPlatformApp(appInstance);
    }

    switch (method.ToLowerInvariant())
    {
      case "get-data":
      {
        var req = await ReadFormRequestAsync(httpRequest, ct);
        var screen = await form.BuildScreenAsync(serviceName, req, ct);
        var entity = await form.GetModelAsync(req, ct);
        var entityId = (entity as IEntity)?.Id ?? req.Id;
        var ver = (entity as IHasVersion)?.Ver ?? 1;

        var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
        if (entity is IReadOnlyEntity && dict.TryGetValue("name", out var nameObj) && nameObj != null)
        {
          var nameStr = nameObj is JsonElement je && je.ValueKind == JsonValueKind.String ? je.GetString() : nameObj.ToString();
          if (!string.IsNullOrEmpty(nameStr))
          {
            dict["name"] = Localizer.Instance[nameStr];
          }
        }
        await form.PopulateReferencesAsync(dict, entity, screen, ct);

        screen.Data = new FormDataDto
        {
          Id = entityId > 0 ? entityId : null,
          Ver = entityId > 0 ? ver : null,
          Value = dict
        };

        return Results.Ok(screen);
      }

      case "save":
      {
        using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        var req = BindFormRequest(httpRequest, doc.RootElement);

        object entity;
        try
        {
          entity = await form.SaveAsync(doc.RootElement, req, ct);
        }
        catch (FormRefusedException ex)
        {
          return ex.Outcome == MutationOutcome.Conflict
            ? Results.Conflict(new { outcome = ex.Outcome, title = ex.Title, messages = ex.Messages })
            : Results.UnprocessableEntity(new { outcome = ex.Outcome, title = ex.Title, messages = ex.Messages });
        }
        catch (DbUpdateConcurrencyException)
        {
          return Results.Conflict(new
          {
            outcome = MutationOutcome.Conflict,
            title = Localizer.Instance["MutationConflict"],
            messages = new[] { new MessageDto { Detail = Localizer.Instance["FormRecordChanged"], Type = MessageType.Error } }
          });
        }
        catch (DbUpdateException)
        {
          return Results.UnprocessableEntity(new
          {
            outcome = MutationOutcome.Refused,
            title = Localizer.Instance["MutationRefused"],
            messages = new[] { new MessageDto { Detail = Localizer.Instance["MutationConstraint"], Type = MessageType.Error } }
          });
        }

        var entityId = (entity as IEntity)?.Id;
        var ver = (entity as IHasVersion)?.Ver;

        var jsonText = JsonSerializer.Serialize(entity, PlatformJsonOptions.DefaultOptions);
        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonText) ?? [];
        req.Id = entityId;
        var screen = await form.BuildScreenAsync(serviceName, req, ct);
        await form.PopulateReferencesAsync(dict, entity, screen, ct);

        return Results.Ok(new FormDataDto
        {
          Id = entityId,
          Ver = ver,
          Value = dict,
          Outcome = MutationOutcome.Committed
        });
      }

      case "move-status" when subMethod != null:
      {
        long? id = null;
        JsonElement? bodyElement = null;
        using var jsonDoc = httpRequest.ContentLength > 0
          ? await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct)
          : null;

        if (jsonDoc != null)
        {
          bodyElement = jsonDoc.RootElement;
          if (bodyElement.Value.TryGetProperty("id", out var idProp) && idProp.TryGetInt64(out var parsedId))
          {
            id = parsedId;
          }
        }

        if (id == null && httpRequest.Query.TryGetValue("id", out var qIdStr) && long.TryParse(qIdStr, out var qId))
        {
          id = qId;
        }

        if (id == null)
        {
          return Results.BadRequest(new { error = "Record id is required for move-status." });
        }

        var result = await form.MoveStatusAsync(id.Value, subMethod, bodyElement, ct);
        if (result.Outcome != MutationOutcome.Committed)
        {
          return result.Outcome == MutationOutcome.Conflict
            ? Results.Conflict(new { outcome = result.Outcome, messages = result.Warnings })
            : Results.UnprocessableEntity(new { outcome = result.Outcome, messages = result.Warnings });
        }

        return Results.Ok(result);
      }

      case "delete":
      {
        var ids = await ExtractIdsAsync(httpRequest, ct);
        int deleted = 0;
        try
        {
          foreach (var id in ids)
          {
            await form.DeleteAsync(id, ct);
            deleted++;
          }
        }
        catch (FormRefusedException ex)
        {
          return ex.Outcome == MutationOutcome.Conflict
            ? Results.Conflict(new { outcome = ex.Outcome, title = ex.Title, messages = ex.Messages })
            : Results.UnprocessableEntity(new { outcome = ex.Outcome, title = ex.Title, messages = ex.Messages });
        }
        catch (DbUpdateException)
        {
          return Results.UnprocessableEntity(new
          {
            outcome = MutationOutcome.Refused,
            title = Localizer.Instance["DeleteRefused"],
            messages = new[] { new MessageDto { Detail = Localizer.Instance["DeleteRefused"], Type = MessageType.Error } }
          });
        }
        return Results.Ok(new GridDeleteDto { Deleted = deleted, Outcome = MutationOutcome.Committed });
      }

      default:
        return Results.NotFound();
    }
  }

  private static bool IsCustomActionPermitted(MethodInfo actionMethod, Feature? feat, IUserContext? userContext)
  {
    if (userContext == null || !userContext.IsAuthenticated)
    {
      return true;
    }

    var reqAttr = actionMethod.GetCustomAttribute<Security.RequireRightAttribute>();
    if (reqAttr != null)
    {
      return !Feature.FeaturesByServiceType.TryGetValue(reqAttr.Service, out var targetFeat) ||
             userContext.Can(targetFeat.Action(reqAttr.Action.ToString())) ||
             userContext.Can(targetFeat.All) ||
             userContext.Can("*");
    }

    var targetKey = feat?.Edit?.Key;
    return targetKey != null
      ? userContext.Can(targetKey) || userContext.Can(feat!.All) || userContext.Can("*")
      : userContext.Can("*");
  }

  private static async Task<IResult> InvokeServiceMethodAsync(MethodInfo method, object target, object?[] args)
  {
    var resultTask = method.Invoke(target, args);
    if (resultTask is Task<IResult> taskResult)
    {
      return await taskResult;
    }
    if (resultTask is Task<GridActionDto> actionTask)
    {
      return PlatformEndpoints.ToMutationResult(await actionTask);
    }
    if (resultTask is Task task)
    {
      await task.ConfigureAwait(false);
      var result = task.GetType().GetProperty("Result")?.GetValue(task);
      if (result is IResult r) return r;
      return PlatformEndpoints.ToMutationResult(result ?? new GridActionDto { Outcome = MutationOutcome.Committed });
    }
    if (resultTask is IResult immediateResult)
    {
      return immediateResult;
    }
    return PlatformEndpoints.ToMutationResult(resultTask ?? new GridActionDto { Outcome = MutationOutcome.Committed });
  }

  private static async Task<T> ReadGridRequestAsync<T>(HttpRequest httpRequest, CancellationToken ct)
    where T : GridRequest, new()
  {
    T? req = null;
    if (httpRequest.ContentLength > 0)
    {
      try { req = await httpRequest.ReadFromJsonAsync<T>(cancellationToken: ct); } catch { }
    }
    req ??= new T();
    ApplyGridQueryParams(httpRequest, req);
    return req;
  }

  private static async Task<GridRequest> ReadGridRequestAsync(HttpRequest httpRequest, Type requestType, CancellationToken ct)
  {
    GridRequest? req = null;
    if (httpRequest.ContentLength > 0)
    {
      try { req = (GridRequest?)await httpRequest.ReadFromJsonAsync(requestType, cancellationToken: ct); } catch { }
    }
    req ??= (GridRequest)Activator.CreateInstance(requestType)!;
    ApplyGridQueryParams(httpRequest, req);
    return req;
  }

  private static void ApplyGridQueryParams(HttpRequest httpRequest, GridRequest req)
  {
    if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var idVal)) req.Id = idVal;
    if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal)) req.ParentId = pidVal;
    if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;
  }

  private static async Task<FormRequest> ReadFormRequestAsync(HttpRequest httpRequest, CancellationToken ct)
  {
    if (httpRequest.ContentLength > 0)
    {
      try
      {
        using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        return BindFormRequest(httpRequest, doc.RootElement);
      }
      catch { }
    }
    return BindFormRequest(httpRequest, default);
  }

  private static FormRequest BindFormRequest(HttpRequest httpRequest, JsonElement root)
  {
    var req = new FormRequest();
    if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId)) req.Id = parsedId;
    if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var parsedPid)) req.ParentId = parsedPid;
    if (httpRequest.Query.TryGetValue("parentService", out var psStr)) req.ParentService = psStr;

    if (root.ValueKind == JsonValueKind.Object)
    {
      if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var bodyId)) req.Id = bodyId;
      if (root.TryGetProperty("ver", out var verProp) && verProp.ValueKind == JsonValueKind.Number && verProp.TryGetInt32(out var bodyVer)) req.Ver = bodyVer;
      if (root.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var bodyPid)) req.ParentId = bodyPid;
      if (root.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } bodyPs) req.ParentService = bodyPs;
    }

    return req;
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
}
