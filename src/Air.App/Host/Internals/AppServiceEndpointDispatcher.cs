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

        var req = await ReadReportRequestAsync(httpRequest, ct);
        if (req.IsDataOnly)
        {
          return Results.Ok(new ScreenDto { Data = await reportService.GetReportAsync(req, ct) });
        }

        return Results.Ok(await reportService.BuildScreenAsync(serviceName, req, ct));
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

    switch (method.ToLowerInvariant())
    {
      case "get-data":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          var isPicker = req.IsReferencePicker || req.ReferenceLabelPaths is { Count: > 0 };
          var canView = grid.CanView(req, userContext);
          if (!canView && (!isPicker || !grid.CanLookup(req, userContext)))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          if (!canView)
          {
            req.IsReferencePicker = true;
            req.IsDataOnly = true;
            req.Filter = ReferenceLookupAllowlist.SanitizeFilter(serviceType, req.Filter);
            req.ColumnFilter = null;
            req.NamedFilters = null;
            req.PatchSourceColumns = ReferenceLookupAllowlist.FilterPatchColumns(serviceType, req.PatchSourceColumns);
            req.ReferenceLabelPaths = ReferenceLookupAllowlist.FilterLabelPaths(serviceType, grid.GetReferenceLabelPaths(), req.ReferenceLabelPaths);
            req.PageSize = Math.Min(req.PageSize > 0 ? req.PageSize : 50, 50);
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
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          var isPicker = req.IsReferencePicker || req.ReferenceLabelPaths is { Count: > 0 };
          var canView = grid.CanView(req, userContext);
          if (!canView && (!isPicker || !grid.CanLookup(req, userContext)))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          if (!canView)
          {
            req.IsReferencePicker = true;
            req.Filter = ReferenceLookupAllowlist.SanitizeFilter(serviceType, req.Filter);
            req.ColumnFilter = null;
            req.NamedFilters = null;
          }

          return Results.Ok(await grid.GetCountAsync(req, ct));
        }

      case "get-one":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          if (req.Id is not > 0)
          {
            return Results.NotFound();
          }

          var row = await grid.ReadRowAsync(req.Id.Value, ct);
          return row is null ? Results.NotFound() : Results.Ok(row);
        }

      case "delete":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
          return PlatformEndpointsExtensions.ToMutationResult(await grid.Delete(req, ct));
        }

      case "save-cell":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          var req = await ReadGridRequestAsync<GridCellRequest>(httpRequest, ct);
          return PlatformEndpointsExtensions.ToMutationResult(await grid.SaveCellAsync(req, services, ct));
        }

      case "get-action-input":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          GridRequest? req = null;
          var actionMethodKey = subMethod ?? "";
          if (httpRequest.ContentLength > 0)
          {
            try
            {
              using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
              if (string.IsNullOrEmpty(actionMethodKey) && doc.RootElement.TryGetProperty("method", out var mProp) && mProp.ValueKind == JsonValueKind.String)
              {
                actionMethodKey = mProp.GetString() ?? "";
              }

              req = JsonSerializer.Deserialize<GridRequest>(doc.RootElement.GetRawText(), PlatformJsonOptions.DefaultOptions);
            }
            catch { }
          }

          req ??= new GridRequest();
          ApplyGridQueryParams(httpRequest, req);
          var defaults = await grid.GetActionInputAsync(actionMethodKey, req, ct);
          return Results.Ok(ToActionInputDto(defaults));
        }

      case "move-status":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

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
          return PlatformEndpointsExtensions.ToMutationResult(await grid.MoveStatusAsync(req, subMethod ?? "", bodyElement, ct));
        }

      case "export":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          if (!CanExport(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var req = await ReadGridRequestAsync<GridRequest>(httpRequest, ct);
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

    switch (method.ToLowerInvariant())
    {
      case "get-data":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          var req = await ReadFormRequestAsync(httpRequest, ct);
          var screen = await form.BuildScreenAsync(serviceName, req, ct);
          var entity = await form.GetModelAsync(req, ct);
          screen.Data = await form.BuildFormDataAsync(entity, screen, ct: ct);

          return Results.Ok(screen);
        }

      case "save":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
          var req = BindFormRequest(httpRequest, doc.RootElement);

          object entity;
          try
          {
            entity = await form.SaveAsync(doc.RootElement, req, ct);
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
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          if (!CanView(feat, userContext))
          {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
          }

          FormRequest? req = null;
          var actionMethodKey = subMethod ?? "";
          if (httpRequest.ContentLength > 0)
          {
            try
            {
              using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
              if (string.IsNullOrEmpty(actionMethodKey) && doc.RootElement.TryGetProperty("method", out var mProp) && mProp.ValueKind == JsonValueKind.String)
              {
                actionMethodKey = mProp.GetString() ?? "";
              }

              req = BindFormRequest(httpRequest, doc.RootElement);
            }
            catch { }
          }

          req ??= BindFormRequest(httpRequest, default);
          var defaults = await form.GetActionInputAsync(actionMethodKey, req, ct);
          return Results.Ok(ToActionInputDto(defaults));
        }

      case "move-status" when subMethod != null:
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          JsonElement? bodyElement = null;
          using var jsonDoc = httpRequest.ContentLength > 0
            ? await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct)
            : null;

          if (jsonDoc != null)
          {
            bodyElement = jsonDoc.RootElement;
          }

          var req = BindFormRequest(httpRequest, bodyElement ?? default);
          if (req.Id == null)
          {
            return Results.BadRequest(new { detail = "Record id is required for move-status." });
          }

          form.SetRequest(req);
          var result = await form.MoveStatusAsync(req.Id.Value, subMethod, bodyElement, req.Ver, ct);
          return PlatformEndpointsExtensions.ToMutationResult(result);
        }

      case "delete":
        {
          if (userContext is null or { IsAuthenticated: false })
          {
            return Results.Unauthorized();
          }

          var (req, ids) = await ExtractDeleteRequestAsync(httpRequest, ct);
          form.SetRequest(req);
          var deleted = 0;
          try
          {
            await form.DbContext.RunMutationAsync(async mutationCt =>
            {
              foreach (var id in ids)
              {
                form.SetRequest(new FormRequest
                {
                  Id = id,
                  ParentId = req.ParentId,
                  ParentService = req.ParentService,
                  Ver = ids.Count == 1 ? req.Ver : null
                });
                await form.DeleteAsync(id, mutationCt);
                deleted++;
              }

              return true;
            }, ct: ct);
          }
          catch (FormRefusedException ex)
          {
            return ex.Outcome == MutationOutcome.Conflict
              ? Results.Conflict(new MutationResultDto { Outcome = ex.Outcome, Title = ex.Title, Messages = ex.Messages.ToList() })
              : Results.UnprocessableEntity(new MutationResultDto { Outcome = ex.Outcome, Title = ex.Title, Messages = ex.Messages.ToList() });
          }
          catch (DbUpdateException)
          {
            return Results.UnprocessableEntity(new MutationResultDto
            {
              Outcome = MutationOutcome.Refused,
              Title = Localizer.Instance["DeleteRefused"],
              Messages = [new MessageDto { Detail = Localizer.Instance["DeleteRefused"], Type = MessageType.Error }]
            });
          }

          return Results.Ok(new GridDeleteDto { Deleted = deleted, Outcome = MutationOutcome.Committed });
        }

      default:
        return await DispatchCustomServiceMethodAsync(serviceType, form, method, feat, httpRequest, services, ct);
    }
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
      ApplyGridQueryParams(httpRequest, gReq);
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
      var bound = BindFormRequest(httpRequest, rawBody ?? default);
      fReq.Id ??= bound.Id;
      fReq.Ver ??= bound.Ver;
      fReq.ParentId ??= bound.ParentId;
      fReq.ParentService ??= bound.ParentService;
      fReq.Value ??= bound.Value;
      return fReq;
    }

    return null;
  }

  private static bool CanView(Feature? feat, IUserContext? userContext) =>
    ServiceEndpointsExtensions.CanView(feat, userContext);

  private static bool CanExport(Feature? feat, IUserContext? userContext) =>
    ServiceEndpointsExtensions.CanExport(feat, userContext);

  private static bool IsCustomActionPermitted(MethodInfo actionMethod, Feature? feat, IUserContext? userContext) =>
    ServiceEndpointsExtensions.IsCustomActionPermitted(actionMethod, feat, userContext);

  private static Task<IResult> InvokeServiceMethodAsync(MethodInfo method, object target, object?[] args) =>
    ServiceEndpointsExtensions.InvokeServiceMethodAsync(method, target, args);

  private static async Task<T> ReadGridRequestAsync<T>(HttpRequest httpRequest, CancellationToken ct)
    where T : GridRequest, new()
  {
    T? req = null;
    if (httpRequest.ContentLength > 0)
    {
      try
      {
        req = await httpRequest.ReadFromJsonAsync<T>(cancellationToken: ct);
      }
      catch { }
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
      try
      {
        req = (GridRequest?)await httpRequest.ReadFromJsonAsync(requestType, cancellationToken: ct);
      }
      catch { }
    }

    req ??= (GridRequest)Activator.CreateInstance(requestType)!;
    ApplyGridQueryParams(httpRequest, req);
    return req;
  }

  private static void ApplyGridQueryParams(HttpRequest httpRequest, GridRequest req)
  {
    if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var idVal))
    {
      req.Id = idVal;
    }

    if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var pidVal))
    {
      req.ParentId = pidVal;
    }

    if (httpRequest.Query.TryGetValue("parentService", out var psStr))
    {
      req.ParentService = psStr;
    }
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
    if (httpRequest.Query.TryGetValue("id", out var idStr) && long.TryParse(idStr, out var parsedId))
    {
      req.Id = parsedId;
    }

    if (httpRequest.Query.TryGetValue("parentId", out var pidStr) && long.TryParse(pidStr, out var parsedPid))
    {
      req.ParentId = parsedPid;
    }

    if (httpRequest.Query.TryGetValue("parentService", out var psStr))
    {
      req.ParentService = psStr;
    }

    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      req.IdempotencyKey = idemVals.ToString().Trim();
    }

    if (root.ValueKind == JsonValueKind.Object)
    {
      req.RawPayload = root.GetRawText();

      if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var bodyId))
      {
        req.Id = bodyId;
      }

      if (root.TryGetProperty("ver", out var verProp) && verProp.ValueKind == JsonValueKind.Number && verProp.TryGetInt32(out var bodyVer))
      {
        req.Ver = bodyVer;
      }

      if (root.TryGetProperty("parentId", out var pidProp) && pidProp.ValueKind == JsonValueKind.Number && pidProp.TryGetInt64(out var bodyPid))
      {
        req.ParentId = bodyPid;
      }

      if (root.TryGetProperty("parentService", out var psProp) && psProp.ValueKind == JsonValueKind.String && psProp.GetString() is { } bodyPs)
      {
        req.ParentService = bodyPs;
      }

      if (root.TryGetProperty("value", out var valProp) && valProp.ValueKind == JsonValueKind.Object)
      {
        req.Value = valProp.Clone();
      }
    }

    return req;
  }

  private static async Task<(FormRequest Request, List<long> Ids)> ExtractDeleteRequestAsync(HttpRequest request, CancellationToken ct)
  {
    List<long> ids = [];
    FormRequest? req = null;

    if (request.ContentLength > 0)
    {
      try
      {
        using var doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        req = BindFormRequest(request, doc.RootElement);
        if (doc.RootElement.TryGetProperty("selectedIds", out var sel) && sel.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in sel.EnumerateArray())
          {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id))
            {
              ids.Add(id);
            }
            else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var sid))
            {
              ids.Add(sid);
            }
          }
        }
        else if (doc.RootElement.TryGetProperty("id", out var idProp))
        {
          if (idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var id))
          {
            ids.Add(id);
          }
          else if (idProp.ValueKind == JsonValueKind.String && long.TryParse(idProp.GetString(), out var sid))
          {
            ids.Add(sid);
          }
        }
      }
      catch { }
    }

    req ??= BindFormRequest(request, default);
    if (ids.Count == 0 && req.Id is > 0)
    {
      ids.Add(req.Id.Value);
    }

    return (req, ids);
  }

  private static async Task<ReportRequest> ReadReportRequestAsync(HttpRequest request, CancellationToken ct)
  {
    if (request.ContentLength is null or 0)
    {
      return new ReportRequest();
    }

    try
    {
      return await request.ReadFromJsonAsync<ReportRequest>(PlatformJsonOptions.DefaultOptions, ct) ?? new ReportRequest();
    }
    catch
    {
      return new ReportRequest();
    }
  }
}
