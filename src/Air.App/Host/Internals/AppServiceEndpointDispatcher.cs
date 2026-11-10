using Platform.Api.Host.Internals;
using Platform.App.Internals;

namespace Platform.App.Host.Internals;

internal sealed class AppServiceEndpointDispatcher : IServiceEndpointDispatcher
{
  public bool CanDispatch(Type serviceType) =>
    typeof(IGridService).IsAssignableFrom(serviceType) ||
    typeof(IFormService).IsAssignableFrom(serviceType) ||
    typeof(IReportService).IsAssignableFrom(serviceType) ||
    typeof(IScreenService).IsAssignableFrom(serviceType);

  public async Task<IResult> DispatchAsync(
    Type serviceType,
    string serviceName,
    string method,
    string? subMethod,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    services.GetService<DbScope>()?.Bind(ModuleCatalog.GetServiceDbContextType(serviceType));

    if (typeof(IGridService).IsAssignableFrom(serviceType))
    {
      return await DispatchGridAsync(serviceType, serviceName, method, subMethod, feat, httpRequest, services, ct);
    }

    if (typeof(IFormService).IsAssignableFrom(serviceType))
    {
      return await DispatchFormAsync(serviceType, serviceName, method, subMethod, feat, httpRequest, services, ct);
    }

    if (typeof(IReportService).IsAssignableFrom(serviceType))
    {
      var reportService = (IReportService)services.GetRequiredService(serviceType);
      feat ??= reportService.Feature;
      if (string.Equals(method, "get-data", StringComparison.OrdinalIgnoreCase))
      {
        var userContext = services.GetService<IUserContext>();
        if (userContext is null or { IsAuthenticated: false })
        {
          return Results.Unauthorized();
        }

        if (!CanView(feat, userContext))
        {
          return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var (req, isMalformed) = await ReadJsonOrDefaultAsync<ReportRequest>(httpRequest, ct);
        if (isMalformed)
        {
          return Results.BadRequest(new { detail = "Malformed JSON request body." });
        }

        return req.IsDataOnly
          ? Results.Ok(new ScreenDto { Data = await reportService.GetReportAsync(req, ct) })
          : Results.Ok(await reportService.BuildScreenAsync(serviceName, req, ct));
      }

      return await DispatchCustomServiceMethodAsync(serviceType, reportService, method, feat, httpRequest, services, ct);
    }

    if (typeof(IScreenService).IsAssignableFrom(serviceType))
    {
      var screenService = (IScreenService)services.GetRequiredService(serviceType);
      feat ??= screenService.Feature;
      if (string.Equals(method, "get-data", StringComparison.OrdinalIgnoreCase))
      {
        var userContext = services.GetService<IUserContext>();
        if (userContext is null or { IsAuthenticated: false })
        {
          return Results.Unauthorized();
        }

        if (!CanView(feat, userContext))
        {
          return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await screenService.BuildScreenAsync(serviceName, ct));
      }

      return await DispatchCustomServiceMethodAsync(serviceType, screenService, method, feat, httpRequest, services, ct);
    }

    return Results.NotFound();
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
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
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
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
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
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          if (!grid.CanView(req, userContext!))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          if (req.Id is not > 0)
          {
            return Results.NotFound();
          }

          if (!await grid.BindParentAsync(req.ParentService, req.ParentId, ct))
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
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          return PlatformEndpointsExtensions.ToMutationResult(await grid.Delete(req, ct));
        }

      case "save-cell":
        {
          var (req, isMalformed) = await ReadGridRequestAsync<GridCellRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          return PlatformEndpointsExtensions.ToMutationResult(await grid.SaveCellAsync(req, services, ct));
        }

      case "get-action-input":
        {
          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed || !TryDeserializeOptional(body, out GridRequest req))
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          var actionMethodKey = ResolveActionMethodKey(subMethod, body);
          ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
          var defaults = await grid.GetActionInputAsync(actionMethodKey, req, ct);
          return Results.Ok(ToActionInputDto(defaults));
        }

      case "move-status":
        {
          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed || !TryDeserializeOptional(body, out GridRequest req))
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
          return PlatformEndpointsExtensions.ToMutationResult(await grid.MoveStatusAsync(req, subMethod ?? "", body, ct));
        }

      case "export":
        {
          if (!CanExport(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (req, isMalformed) = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
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

            if (!IsCustomActionPermitted(uploadMethod, feat, userContext))
            {
              return Results.UnprocessableEntity(new MutationResultDto
              {
                Outcome = MutationOutcome.Refused,
                Title = Localizer.Instance["MutationRefused"],
                Messages = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }]
              });
            }

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

            return await InvokeServiceMethodAsync(uploadMethod, grid, [uploadReq, formCollection.Files, ct]);
          }

          break;
        }
    }

    return await DispatchCustomServiceMethodAsync(serviceType, grid, method, feat, httpRequest, services, ct);
  }

  private static async Task<IResult> DispatchFormAsync(
    Type serviceType,
    string serviceName,
    string method,
    string? subMethod,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var form = (IFormService)services.GetRequiredService(serviceType);
    feat ??= form.Feature;
    if (services.GetService<PlatformAppService>() is { } appInstance)
    {
      form.AttachPlatformApp(appInstance);
    }

    var userContext = services.GetService<IUserContext>();
    var lowerMethod = method.ToLowerInvariant();
    if (IsBuiltInFormMethod(lowerMethod, subMethod) && userContext is null or { IsAuthenticated: false })
    {
      return Results.Unauthorized();
    }

    switch (lowerMethod)
    {
      case "get-data":
        {
          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (req, isMalformed) = await ReadFormRequestAsync(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          var screen = await form.BuildScreenAsync(serviceName, req, ct);
          var entity = await form.GetModelAsync(req, ct);
          screen.Data = await form.BuildFormDataAsync(entity, screen, ct: ct);

          return Results.Ok(screen);
        }

      case "save":
        {
          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed || !body.HasValue)
          {
            return Results.BadRequest(new { detail = "Malformed or empty JSON request body." });
          }

          var req = BindFormRequest(httpRequest, body.Value);

          object entity;
          try
          {
            entity = await form.SaveAsync(body.Value, req, ct);
          }
          catch (JsonException)
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }
          catch (IdempotencyConflictException ex)
          {
            return Results.UnprocessableEntity(new MutationResultDto
            {
              Outcome = MutationOutcome.Refused,
              Title = Localizer.Instance["MutationRefused"],
              Messages = [new MessageDto { Detail = ex.Message, Type = MessageType.Error }]
            });
          }
          catch (FormRefusedException ex)
          {
            return ex.Outcome == MutationOutcome.Conflict
              ? Results.Conflict(new MutationResultDto { Outcome = ex.Outcome, Title = ex.Title, Messages = ex.Messages.ToList() })
              : Results.UnprocessableEntity(new MutationResultDto { Outcome = ex.Outcome, Title = ex.Title, Messages = ex.Messages.ToList() });
          }
          catch (DbUpdateConcurrencyException)
          {
            return Results.Conflict(new MutationResultDto
            {
              Outcome = MutationOutcome.Conflict,
              Title = Localizer.Instance["MutationConflict"],
              Messages = [new MessageDto { Detail = Localizer.Instance["FormRecordChanged"], Type = MessageType.Error }]
            });
          }
          catch (DbUpdateException)
          {
            return Results.UnprocessableEntity(new MutationResultDto
            {
              Outcome = MutationOutcome.Refused,
              Title = Localizer.Instance["MutationRefused"],
              Messages = [new MessageDto { Detail = Localizer.Instance["MutationConstraint"], Type = MessageType.Error }]
            });
          }

          return Results.Ok(await form.BuildFormDataAsync(entity, outcome: MutationOutcome.Committed, ct: ct));
        }

      case "get-action-input":
        {
          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          var actionMethodKey = ResolveActionMethodKey(subMethod, body);
          var req = BindFormRequest(httpRequest, body ?? default);
          var defaults = await form.GetActionInputAsync(actionMethodKey, req, ct);
          return Results.Ok(ToActionInputDto(defaults));
        }

      case "move-status" when subMethod != null:
        {
          var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
          if (isMalformed)
          {
            return Results.BadRequest(new { detail = "Malformed JSON request body." });
          }

          var req = BindFormRequest(httpRequest, body ?? default);
          if (req.Id == null)
          {
            return Results.BadRequest(new { detail = "Record id is required for move-status." });
          }

          form.SetRequest(req);
          var result = await form.MoveStatusAsync(req.Id.Value, subMethod, body, req.Ver, ct);
          return PlatformEndpointsExtensions.ToMutationResult(result);
        }

      default:
        return await DispatchCustomServiceMethodAsync(serviceType, form, method, feat, httpRequest, services, ct);
    }
  }

  private static bool IsBuiltInGridMethod(string lowerMethod) =>
    lowerMethod is "get-data" or "get-count" or "get-one" or "delete" or "save-cell" or "get-action-input" or "move-status" or "export";

  private static bool IsBuiltInFormMethod(string lowerMethod, string? subMethod) =>
    lowerMethod is "get-data" or "save" or "get-action-input" ||
    lowerMethod == "move-status" && subMethod != null;

  private static string ResolveActionMethodKey(string? subMethod, JsonElement? body)
  {
    if (!string.IsNullOrEmpty(subMethod))
    {
      return subMethod;
    }

    if (body is { ValueKind: JsonValueKind.Object } root &&
        root.TryGetProperty("method", out var mProp) &&
        mProp.ValueKind == JsonValueKind.String)
    {
      return mProp.GetString() ?? "";
    }

    return "";
  }

  private static ActionInputDto ToActionInputDto(object? defaults)
  {
    if (defaults is Dictionary<string, object?> dict)
    {
      return new ActionInputDto { Value = dict };
    }

    if (defaults is null)
    {
      return new ActionInputDto { Value = [] };
    }

    var json = JsonSerializer.Serialize(defaults, PlatformJsonOptions.DefaultOptions);
    var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, PlatformJsonOptions.DefaultOptions) ?? [];
    return new ActionInputDto { Value = parsed };
  }

  private static Task<IResult> DispatchCustomServiceMethodAsync(
    Type serviceType,
    object serviceInstance,
    string method,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct) =>
    ServiceEndpointsExtensions.DispatchCustomServiceMethodAsync(
      serviceType,
      serviceInstance,
      method,
      feat,
      httpRequest,
      services,
      ct,
      BindAppRequestParameter);

  private static object? BindAppRequestParameter(Type pType, HttpRequest httpRequest, JsonElement? rawBody)
  {
    if (typeof(GridRequest).IsAssignableFrom(pType))
    {
      var gReq = rawBody.HasValue
        ? (GridRequest?)JsonSerializer.Deserialize(rawBody.Value.GetRawText(), pType, PlatformJsonOptions.DefaultOptions)
        : null;
      gReq ??= (GridRequest)Activator.CreateInstance(pType)!;
      ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, gReq);
      return gReq;
    }

    if (typeof(FormRequest).IsAssignableFrom(pType))
    {
      if (pType == typeof(FormRequest))
      {
        return BindFormRequest(httpRequest, rawBody ?? default);
      }

      var fReq = rawBody.HasValue
        ? (FormRequest?)JsonSerializer.Deserialize(rawBody.Value.GetRawText(), pType, PlatformJsonOptions.DefaultOptions)
        : null;
      fReq ??= (FormRequest)Activator.CreateInstance(pType)!;
      return BindFormRequest(httpRequest, rawBody ?? default, existing: fReq);
    }

    return null;
  }

  private static FormRequest BindFormRequest(
    HttpRequest httpRequest,
    JsonElement root,
    FormRequest? existing = null)
  {
    var (req, _) = FormRequest.Normalize(root, existing, allowNestedFallback: false);
    ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      req.IdempotencyKey = idemVals.ToString().Trim();
    }

    return req;
  }

  private static bool TryDeserializeOptional<T>(JsonElement? body, out T result)
    where T : class, new()
  {
    if (!body.HasValue)
    {
      result = new T();
      return true;
    }

    try
    {
      result = JsonSerializer.Deserialize<T>(body.Value.GetRawText(), PlatformJsonOptions.DefaultOptions) ?? new T();
      return true;
    }
    catch (JsonException)
    {
      result = new T();
      return false;
    }
  }

  private static bool CanView(Feature? feat, IUserContext? userContext) =>
    ServiceEndpointsExtensions.CanView(feat, userContext);

  private static bool CanExport(Feature? feat, IUserContext? userContext) =>
    ServiceEndpointsExtensions.CanExport(feat, userContext);

  private static bool IsCustomActionPermitted(MethodInfo actionMethod, Feature? feat, IUserContext? userContext) =>
    ServiceEndpointsExtensions.IsCustomActionPermitted(actionMethod, feat, userContext);

  private static Task<IResult> InvokeServiceMethodAsync(MethodInfo method, object target, object?[] args) =>
    ServiceEndpointsExtensions.InvokeServiceMethodAsync(method, target, args);

  private static async Task<bool> AuthorizeAndSanitizeGridReadAsync(
    Type serviceType,
    IGridService grid,
    GridRequest req,
    IUserContext userContext,
    IServiceProvider services,
    CancellationToken ct)
  {
    var isPicker = req.IsReferencePicker || req.ReferenceLabelPaths is { Count: > 0 };
    var canView = grid.CanView(req, userContext);
    if (!canView && (!isPicker || !grid.CanLookup(req, userContext)))
    {
      return false;
    }

    if (!canView)
    {
      await ReferenceLookupAllowlist.EnsureWarmAsync(services, ct);
      req.IsReferencePicker = true;
      req.IsDataOnly = true;
      req.Filter = ReferenceLookupAllowlist.SanitizeFilter(serviceType, req.Filter);
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
    var (req, isMalformed) = await ReadJsonOrDefaultAsync<T>(httpRequest, ct);
    ServiceRequestBinder.ApplyScopedQueryParams(httpRequest, req);
    return (req, isMalformed);
  }

  private static async Task<(FormRequest Request, bool IsMalformed)> ReadFormRequestAsync(HttpRequest httpRequest, CancellationToken ct)
  {
    var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
    return (BindFormRequest(httpRequest, body ?? default), isMalformed);
  }

  private static async Task<(T Request, bool IsMalformed)> ReadJsonOrDefaultAsync<T>(HttpRequest httpRequest, CancellationToken ct)
    where T : class, new()
  {
    var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
    if (isMalformed)
    {
      return (new T(), true);
    }

    if (!body.HasValue)
    {
      return (new T(), false);
    }

    try
    {
      var req = JsonSerializer.Deserialize<T>(body.Value.GetRawText(), PlatformJsonOptions.DefaultOptions) ?? new T();
      return (req, false);
    }
    catch (JsonException)
    {
      return (new T(), true);
    }
  }
}
