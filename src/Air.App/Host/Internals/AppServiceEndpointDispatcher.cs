using Platform.Api.Host.Internals;

namespace Platform.App.Host.Internals;

internal sealed class AppServiceEndpointDispatcher : IServiceEndpointDispatcher
{
  public bool CanDispatch(Type serviceType) =>
    typeof(IGridService).IsAssignableFrom(serviceType) ||
    typeof(IFormService).IsAssignableFrom(serviceType) ||
    typeof(IReportService).IsAssignableFrom(serviceType) ||
    typeof(IScreenService).IsAssignableFrom(serviceType);

  public void MapServiceEndpoints(
    RouteGroupBuilder api,
    ModuleCatalog catalog,
    Type serviceType,
    string serviceName,
    Feature? feature) =>
    AppServiceRouteMapper.MapServiceEndpoints(this, api, catalog, serviceType, serviceName, feature);

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
    try
    {
      services.GetService<DbScope>()?.Bind(ModuleCatalog.GetServiceDbContextType(serviceType));

      if (typeof(IGridService).IsAssignableFrom(serviceType))
      {
        return await GridEndpointHandler.DispatchAsync(serviceType, serviceName, method, subMethod, feat, httpRequest, services, ct);
      }

      if (typeof(IFormService).IsAssignableFrom(serviceType))
      {
        return await FormEndpointHandler.DispatchAsync(serviceType, serviceName, method, subMethod, feat, httpRequest, services, ct);
      }

      if (typeof(IReportService).IsAssignableFrom(serviceType))
      {
        return await DispatchReportAsync(serviceType, serviceName, method, feat, httpRequest, services, ct);
      }

      if (typeof(IScreenService).IsAssignableFrom(serviceType))
      {
        return await DispatchScreenAsync(serviceType, serviceName, method, feat, httpRequest, services, ct);
      }

      return Results.NotFound();
    }
    catch (Exception ex) when (PlatformEndpointsExtensions.TryMapException(ex, out var mapped))
    {
      return mapped;
    }
  }

  internal static Task<IResult> DispatchCustomServiceMethodAsync(
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

  internal static string ResolveActionMethodKey(string? subMethod, JsonElement? body)
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

  internal static ActionInputDto ToActionInputDto(object? defaults)
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

  internal static bool TryDeserializeOptional<T>(JsonElement? body, out T result)
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

  private static async Task<IResult> DispatchReportAsync(
    Type serviceType,
    string serviceName,
    string method,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var reportService = (IReportService)services.GetRequiredService(serviceType);
    feat ??= reportService.Feature;
    if (services.GetService<PlatformAppService>() is { } appInstance)
    {
      reportService.AttachPlatformApp(appInstance);
    }

    if (string.Equals(method, "get-data", StringComparison.OrdinalIgnoreCase))
    {
      var userContext = services.GetService<IUserContext>();
      if (userContext is null or { IsAuthenticated: false })
      {
        return Results.Unauthorized();
      }

      if (!ServiceEndpointsExtensions.CanView(feat, userContext))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      var (req, isMalformed) = await ReadJsonOrDefaultAsync<ReportRequest>(httpRequest, ct);
      if (isMalformed)
      {
        return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
      }

      return req.IsDataOnly
        ? Results.Ok(new ScreenDto { Data = await reportService.GetReportAsync(req, ct) })
        : Results.Ok(await reportService.BuildScreenAsync(serviceName, req, ct));
    }

    return await DispatchCustomServiceMethodAsync(serviceType, reportService, method, feat, httpRequest, services, ct);
  }

  private static async Task<IResult> DispatchScreenAsync(
    Type serviceType,
    string serviceName,
    string method,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var screenService = (IScreenService)services.GetRequiredService(serviceType);
    feat ??= screenService.Feature;
    if (services.GetService<PlatformAppService>() is { } appInstance)
    {
      screenService.AttachPlatformApp(appInstance);
    }

    if (string.Equals(method, "get-data", StringComparison.OrdinalIgnoreCase))
    {
      var userContext = services.GetService<IUserContext>();
      if (userContext is null or { IsAuthenticated: false })
      {
        return Results.Unauthorized();
      }

      if (!ServiceEndpointsExtensions.CanView(feat, userContext))
      {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
      }

      var screen = await screenService.BuildScreenAsync(serviceName, ct);
      ScreenService.EnforceAndFilterScreen(
        serviceType,
        screen,
        userContext,
        services.GetService<ModuleCatalog>() ?? ModuleCatalog.Instance);
      return Results.Ok(screen);
    }

    return await DispatchCustomServiceMethodAsync(serviceType, screenService, method, feat, httpRequest, services, ct);
  }

  private static object? BindAppRequestParameter(Type pType, HttpRequest httpRequest, JsonElement? rawBody)
  {
    if (typeof(GridRequest).IsAssignableFrom(pType))
    {
      var gReq = rawBody.HasValue
        ? (GridRequest?)JsonSerializer.Deserialize(rawBody.Value.GetRawText(), pType, PlatformJsonOptions.DefaultOptions)
        : null;
      gReq ??= (GridRequest)Activator.CreateInstance(pType)!;
      GridEndpointHandler.ApplyGridMutationContext(httpRequest, rawBody, gReq);
      return gReq;
    }

    if (typeof(FormRequest).IsAssignableFrom(pType))
    {
      if (pType == typeof(FormRequest))
      {
        return FormEndpointHandler.BindFormRequest(httpRequest, rawBody ?? default);
      }

      var fReq = rawBody.HasValue
        ? (FormRequest?)JsonSerializer.Deserialize(rawBody.Value.GetRawText(), pType, PlatformJsonOptions.DefaultOptions)
        : null;
      fReq ??= (FormRequest)Activator.CreateInstance(pType)!;
      return FormEndpointHandler.BindFormRequest(httpRequest, rawBody ?? default, existing: fReq);
    }

    return null;
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
