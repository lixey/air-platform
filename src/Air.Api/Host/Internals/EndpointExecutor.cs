namespace Platform.Api.Host.Internals;

internal static class EndpointExecutor
{
  private static readonly JsonElement EmptyJsonObject = JsonDocument.Parse("{}").RootElement.Clone();

  public static async Task<IResult> ExecuteAsync(
    EndpointDescriptor descriptor,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    Localizer.Instance.EnsureManagersLoaded();
    if (descriptor.DbContextType is not null)
    {
      services.GetService<DbScope>()?.Bind(descriptor.DbContextType);
    }

    var serviceInstance = services.GetService(descriptor.ServiceType);
    if (serviceInstance is null)
    {
      return Results.NotFound();
    }

    var ps = serviceInstance as IPlatformService;
    ps?.BindContext(services.GetService<PlatformAppService>());
    ps?.ResetExecutionScope();
    var feat = descriptor.StaticFeature ?? ps?.Feature;

    try
    {
      if (descriptor.IsDynamicAction)
      {
        return await ExecuteDynamicAsync(descriptor, serviceInstance, httpRequest, services, ct);
      }

      return await ExecuteReflectedAsync(
        descriptor.Method!,
        descriptor.Parameters,
        descriptor.CompiledInvoker!,
        descriptor.Auth,
        descriptor.IsAnonymous,
        descriptor.RequireClientHeader,
        descriptor.UsesRawHttp,
        descriptor.UsesFormFiles,
        serviceInstance,
        feat,
        httpRequest,
        services,
        ct);
    }
    finally
    {
      ps?.ResetExecutionScope();
    }
  }

  internal static async Task<IResult> DispatchAsync(
    Type serviceType,
    object serviceInstance,
    string actionName,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    var ps = serviceInstance as IPlatformService;
    ps?.BindContext(services.GetService<PlatformAppService>());
    ps?.ResetExecutionScope();
    try
    {
      if (EndpointRegistry.FindReflectedEndpoint(serviceType, actionName) is { CompiledInvoker: not null } reflected)
      {
        return await ExecuteReflectedAsync(
          reflected.Method,
          reflected.Parameters,
          reflected.CompiledInvoker,
          reflected.Auth,
          reflected.IsAnonymous,
          reflected.RequireClientHeader,
          reflected.UsesRawHttp,
          reflected.UsesFormFiles,
          serviceInstance,
          feat ?? ps?.Feature,
          httpRequest,
          services,
          ct);
      }

      if (ps is not null)
      {
        var userContext = services.GetService<IUserContext>();
        if (userContext is null or { IsAuthenticated: false })
        {
          return Results.Unauthorized();
        }

        var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
        if (isMalformed)
        {
          return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
        }

        try
        {
          var dyn = await ps.ExecuteDynamicEndpointAsync(actionName, httpRequest, body, services, ct);
          if (dyn is not null)
          {
            return dyn;
          }
        }
        catch (Exception ex) when (PlatformEndpointsExtensions.TryMapException(ex, out var mapped))
        {
          return mapped;
        }
      }

      return Results.NotFound();
    }
    finally
    {
      ps?.ResetExecutionScope();
    }
  }

  public static async Task<IResult> ExecuteReflectedAsync(
    MethodInfo method,
    EndpointParameter[] parameters,
    Func<object, object?[], Task<IResult>> invoker,
    EndpointAuth auth,
    bool isAnonymous,
    bool requireClientHeader,
    bool usesRawHttp,
    bool usesFormFiles,
    object serviceInstance,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    if (requireClientHeader && !AuthCookieHelper.HasClientHeader(httpRequest))
    {
      return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var userContext = services.GetService<IUserContext>();
    if (!isAnonymous && userContext is null or { IsAuthenticated: false })
    {
      return Results.Unauthorized();
    }

    if (!isAnonymous && CheckAuthFailure(auth, method, feat, userContext) is { } authFailure)
    {
      return authFailure;
    }

    IFormCollection? formCollection = null;
    if (usesFormFiles || httpRequest.HasFormContentType)
    {
      if (!httpRequest.HasFormContentType)
      {
        return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
      }

      formCollection = await httpRequest.ReadFormAsync(ct);
    }

    JsonElement? rawBody = null;
    if (!usesRawHttp && formCollection is null)
    {
      var (parsedBody, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
      if (isMalformed)
      {
        return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
      }

      rawBody = parsedBody;
    }

    var parsedId = ReadQueryOrBodyId(httpRequest, rawBody);
    var args = new object?[parameters.Length];

    try
    {
      for (var i = 0; i < parameters.Length; i++)
      {
        var param = parameters[i];
        args[i] = param.Kind switch
        {
          EndpointParameterKind.CancellationToken => ct,
          EndpointParameterKind.HttpRequest => httpRequest,
          EndpointParameterKind.HttpContext => httpRequest.HttpContext,
          EndpointParameterKind.ServiceProvider => services,
          EndpointParameterKind.FromServices => services.GetService(param.ParameterType)
            ?? (param.HasDefaultValue ? param.DefaultValue : null),
          EndpointParameterKind.FormFiles => formCollection?.Files,
          EndpointParameterKind.RawJsonBody => rawBody,
          EndpointParameterKind.RouteOrBodyId => parsedId ?? (param.HasDefaultValue ? param.DefaultValue : 0L),
          EndpointParameterKind.MultipartBody => BindMultipartRequest(param, httpRequest, formCollection),
          EndpointParameterKind.CustomScopedRequest => BindCustomScopedRequest(param, rawBody, httpRequest),
          EndpointParameterKind.ScopedJsonBody => BindScopedJsonBody(param, rawBody, httpRequest),
          _ => BindJsonBody(param, rawBody)
        };
      }

      return await invoker(serviceInstance, args);
    }
    catch (Exception ex) when (PlatformEndpointsExtensions.TryMapException(ex, out var mapped))
    {
      return mapped;
    }
  }

  private static async Task<IResult> ExecuteDynamicAsync(
    EndpointDescriptor descriptor,
    object serviceInstance,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    if (serviceInstance is not IPlatformService platformService)
    {
      return Results.NotFound();
    }

    var userContext = services.GetService<IUserContext>();
    if (userContext is null or { IsAuthenticated: false })
    {
      return Results.Unauthorized();
    }

    var (body, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
    if (isMalformed)
    {
      return Results.BadRequest(new { detail = Localizer.Instance["InvalidJsonRequest"] });
    }

    try
    {
      var actionName = !string.IsNullOrEmpty(descriptor.ActionName)
        ? descriptor.ActionName
        : httpRequest.RouteValues["__dynAction"] as string ?? string.Empty;
      if (string.IsNullOrEmpty(actionName))
      {
        return Results.NotFound();
      }

      var result = await platformService.ExecuteDynamicEndpointAsync(
        actionName,
        httpRequest,
        body,
        services,
        ct);
      return result ?? Results.NotFound();
    }
    catch (Exception ex) when (PlatformEndpointsExtensions.TryMapException(ex, out var mapped))
    {
      return mapped;
    }
  }

  private static IResult? CheckAuthFailure(
    EndpointAuth auth,
    MethodInfo method,
    Feature? feat,
    IUserContext? userContext) =>
    auth switch
    {
      EndpointAuth.Custom => null,
      EndpointAuth.View => ServiceEndpointsExtensions.CanView(feat, userContext)
        ? null
        : Results.StatusCode(StatusCodes.Status403Forbidden),
      EndpointAuth.Edit => ServiceEndpointsExtensions.CanEdit(feat, userContext)
        ? null
        : Results.StatusCode(StatusCodes.Status403Forbidden),
      EndpointAuth.Export => ServiceEndpointsExtensions.CanExport(feat, userContext)
        ? null
        : Results.StatusCode(StatusCodes.Status403Forbidden),
      _ => ServiceEndpointsExtensions.IsCustomActionPermitted(method, feat, userContext)
        ? null
        : Results.UnprocessableEntity(new MutationResultDto
        {
          Outcome = MutationOutcome.Refused,
          Title = Localizer.Instance["MutationRefused"],
          Messages = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }]
        })
    };

  private static object? BindMultipartRequest(EndpointParameter param, HttpRequest httpRequest, IFormCollection? form)
  {
    var instance = param.Factory?.Invoke();
    if (instance is IMultipartServiceRequest bindable && form is not null)
    {
      bindable.BindMultipart(httpRequest, form);
    }

    return instance;
  }

  private static object? BindCustomScopedRequest(EndpointParameter param, JsonElement? rawBody, HttpRequest httpRequest)
  {
    if (param.Factory?.Invoke() is not IScopedServiceRequest scoped)
    {
      return param.HasDefaultValue ? param.DefaultValue : null;
    }

    if (rawBody.HasValue)
    {
      scoped.BindJsonBody(rawBody.Value);
    }

    ServiceRequestBinder.ApplyScopedHttpMetadata(httpRequest, scoped, rawBody);
    return scoped;
  }

  private static object? BindScopedJsonBody(EndpointParameter param, JsonElement? rawBody, HttpRequest httpRequest)
  {
    var instance = rawBody is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } rb
      ? rb.Deserialize(param.ParameterType, PlatformJsonOptions.DefaultOptions) ?? param.Factory?.Invoke()
      : param.Factory?.Invoke();

    if (instance is IScopedServiceRequest scoped)
    {
      ServiceRequestBinder.ApplyScopedHttpMetadata(httpRequest, scoped, rawBody);
      if (rawBody.HasValue)
      {
        scoped.BindJsonBody(rawBody.Value);
      }
    }

    return instance;
  }

  private static object? BindJsonBody(EndpointParameter param, JsonElement? rawBody)
  {
    if (rawBody is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } rb)
    {
      return rb.Deserialize(param.ParameterType, PlatformJsonOptions.DefaultOptions);
    }

    if (rawBody is { ValueKind: JsonValueKind.Null } || param.HasDefaultValue)
    {
      return param.HasDefaultValue ? param.DefaultValue : null;
    }

    if (!param.ParameterType.IsClass || param.ParameterType == typeof(string))
    {
      return null;
    }

    return EmptyJsonObject.Deserialize(param.ParameterType, PlatformJsonOptions.DefaultOptions)
      ?? param.Factory?.Invoke();
  }

  private static long? ReadQueryOrBodyId(HttpRequest httpRequest, JsonElement? rawBody)
  {
    if (rawBody is { ValueKind: JsonValueKind.Object } rb)
    {
      foreach (var prop in rb.EnumerateObject())
      {
        if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase) &&
            prop.Value.ValueKind == JsonValueKind.Number &&
            prop.Value.TryGetInt64(out var bodyId))
        {
          return bodyId;
        }
      }
    }

    if (httpRequest.Query.TryGetValue("id", out var qIdStr) && long.TryParse(qIdStr, out var qId))
    {
      return qId;
    }

    return null;
  }
}
