using Platform.Api.Host.Internals;
using Platform.App.Internals;

namespace Platform.App.Host.Internals;

internal static class GridEndpointHandler
{
  public static async Task<IResult> DispatchAsync(
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

    var userContext = services.GetService<IUserContext>();
    var lowerMethod = method.ToLowerInvariant();
    if (IsBuiltInGridMethod(lowerMethod) && userContext is null or { IsAuthenticated: false })
    {
      return Results.Unauthorized();
    }

    switch (lowerMethod)
    {
      case "get-data":
        {
          var (req, isMalformed) = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          if (!await AuthorizeAndSanitizeGridReadAsync(serviceType, grid, req, userContext!, services, ct))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          if (req.IsDataOnly)
          {
            return Results.Ok(new ScreenDto { Data = await grid.GetDataAsync(req, ct) });
          }

          var screen = await grid.BuildScreenAsync(serviceName, req, ct);
          if (req.Id is not > 0)
          {
            screen.Data = await grid.GetDataAsync(req, ct);
          }

          return Results.Ok(screen);
        }

      case "get-count":
        {
          var (req, isMalformed) = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          if (!await AuthorizeAndSanitizeGridReadAsync(serviceType, grid, req, userContext!, services, ct))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          return Results.Ok(await grid.GetCountAsync(req, ct));
        }

      case "get-one":
        {
          var (req, isMalformed) = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          if (!grid.CanView(req, userContext!))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          if (req.Id is not > 0 || !await grid.BindParentAsync(req.ParentService, req.ParentId, ct))
          {
            return Results.NotFound();
          }

          var row = await grid.ReadRowAsync(req.Id.Value, ct);
          return row is null ? Results.NotFound() : Results.Ok(row);
        }

      case "delete":
        {
          var (req, isMalformed) = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          return PlatformEndpointsExtensions.ToMutationResult(await grid.Delete(req, ct));
        }

      case "save-cell":
        {
          if (!ServiceEndpointsExtensions.CanEdit(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (req, isMalformed) = await ReadGridRequestAsync<GridCellRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          return PlatformEndpointsExtensions.ToMutationResult(await grid.SaveCellAsync(req, services, ct));
        }

      case "get-action-input":
        {
          if (!ServiceEndpointsExtensions.CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed || !AppServiceEndpointDispatcher.TryDeserializeOptional(body, out GridRequest req))
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          var actionMethodKey = AppServiceEndpointDispatcher.ResolveActionMethodKey(subMethod, body);
          ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
          var defaults = await grid.GetActionInputAsync(actionMethodKey, req, ct);
          return Results.Ok(AppServiceEndpointDispatcher.ToActionInputDto(defaults));
        }

      case "move-status":
        {
          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed || !AppServiceEndpointDispatcher.TryDeserializeOptional(body, out GridRequest req))
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          ApplyGridMutationContext(httpRequest, body, req);
          return PlatformEndpointsExtensions.ToMutationResult(await grid.MoveStatusAsync(req, subMethod ?? "", body, ct));
        }

      case "export":
        {
          if (!ServiceEndpointsExtensions.CanExport(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (req, isMalformed) = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
          }

          return await grid.ExportAsync(req, ct);
        }

      case "upload":
        {
          var uploadMethod = serviceType.GetMethod("Upload");
          if (uploadMethod != null && httpRequest.HasFormContentType)
          {
            if (userContext is null or { IsAuthenticated: false })
            {
              return Results.Unauthorized();
            }

            if (!ServiceEndpointsExtensions.IsCustomActionPermitted(uploadMethod, feat, userContext))
            {
              return Results.UnprocessableEntity(new MutationResultDto
              {
                Outcome = MutationOutcome.Refused,
                Title = Localizer.Instance["MutationRefused"],
                Messages = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }]
              });
            }

            var (uploadReq, files) = await BuildUploadRequestAsync(httpRequest, ct);
            return await ServiceEndpointsExtensions.InvokeServiceMethodAsync(uploadMethod, grid, [uploadReq, files, ct]);
          }

          break;
        }
    }

    if (subMethod == null && ServiceRequestBinder.ResolveCallableActionMethod(serviceType, method) == null)
    {
      if (userContext is null or { IsAuthenticated: false })
      {
        return await grid.HasActionAsync(method, ct) ? Results.Unauthorized() : Results.NotFound();
      }

      var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
      if (isMalformed || !AppServiceEndpointDispatcher.TryDeserializeOptional(body, out GridRequest req))
      {
        return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
      }

      ApplyGridMutationContext(httpRequest, body, req);
      var res = await grid.ExecuteDeclaredActionAsync(method, req, body, ct);
      return res != null ? PlatformEndpointsExtensions.ToMutationResult(res) : Results.NotFound();
    }

    return await AppServiceEndpointDispatcher.DispatchCustomServiceMethodAsync(serviceType, grid, method, feat, httpRequest, services, ct);
  }

  public static void ApplyGridMutationContext(HttpRequest httpRequest, JsonElement? body, GridRequest req)
  {
    ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      req.IdempotencyKey = idemVals.ToString().Trim();
    }

    req.RawPayload = body?.GetRawText();
  }

  private static bool IsBuiltInGridMethod(string lowerMethod) =>
    lowerMethod is "get-data" or "get-count" or "get-one" or "delete" or "save-cell" or "get-action-input" or "move-status" or "export";

  private static async Task<(GridUploadRequest Request, IFormFileCollection Files)> BuildUploadRequestAsync(
    HttpRequest httpRequest,
    CancellationToken ct)
  {
    var formCollection = await httpRequest.ReadFormAsync(ct);
    var uploadReq = new GridUploadRequest();
    if (formCollection.TryGetValue("parentService", out var psVal))
    {
      uploadReq.ParentService = psVal;
    }

    if (formCollection.TryGetValue("parentId", out var pidVal) && long.TryParse(pidVal, out var parsedPid))
    {
      uploadReq.ParentId = parsedPid;
    }

    if (formCollection.TryGetValue("afterId", out var aidVal) && long.TryParse(aidVal, out var parsedAid))
    {
      uploadReq.AfterId = parsedAid;
    }

    if (formCollection.TryGetValue("metadata", out var metaVal))
    {
      uploadReq.Metadata = metaVal;
    }

    if (httpRequest.Query.TryGetValue("parentId", out var qPid) && long.TryParse(qPid, out var parsedQPid))
    {
      uploadReq.ParentId = parsedQPid;
    }

    if (httpRequest.Query.TryGetValue("parentService", out var qPs))
    {
      uploadReq.ParentService = qPs;
    }

    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      uploadReq.IdempotencyKey = idemVals.ToString().Trim();
    }

    var primaryFile = formCollection.Files["file"] ?? formCollection.Files.FirstOrDefault();
    uploadReq.RawPayload = $"{uploadReq.ParentService}|{uploadReq.ParentId}|{uploadReq.AfterId}|{uploadReq.Metadata}|{primaryFile?.FileName}|{primaryFile?.Length}";
    return (uploadReq, formCollection.Files);
  }

  private static async Task<bool> AuthorizeAndSanitizeGridReadAsync(
    Type serviceType,
    IGridService grid,
    GridRequest req,
    IUserContext userContext,
    IServiceProvider services,
    CancellationToken ct)
  {
    var isPicker = req.IsReferencePicker || req.ReferenceLabelPaths is { Count: > 0 };
    if (isPicker)
    {
      await ReferenceLookupAllowlist.EnsureWarmAsync(services, ct);
    }

    var canView = grid.CanView(req, userContext);
    if (!canView && (!isPicker || !grid.CanLookup(req, userContext)))
    {
      return false;
    }

    if (!canView)
    {
      if (!ReferenceLookupAllowlist.TrySanitizeFilter(serviceType, req.Filter, out var sanitizedFilter))
      {
        return false;
      }

      req.IsReferencePicker = true;
      req.IsDataOnly = true;
      req.Filter = sanitizedFilter;
      req.ColumnFilter = null;
      req.NamedFilters = null;
      req.PatchSourceColumns = ReferenceLookupAllowlist.FilterPatchColumns(serviceType, req.PatchSourceColumns);
      req.ReferenceLabelPaths = ReferenceLookupAllowlist.FilterLabelPaths(serviceType, grid.GetReferenceLabelPaths(), req.ReferenceLabelPaths);
      req.PageSize = Math.Min(req.PageSize > 0 ? req.PageSize : 50, 50);
    }

    return true;
  }

  private static async Task<(T Request, bool IsMalformed)> ReadGridRequestAsync<T>(HttpRequest httpRequest, CancellationToken ct)
    where T : GridRequest, new()
  {
    var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
    if (isMalformed || !AppServiceEndpointDispatcher.TryDeserializeOptional(body, out T req))
    {
      return (new T(), true);
    }

    ApplyGridMutationContext(httpRequest, body, req);
    return (req, false);
  }
}
