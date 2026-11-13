namespace Platform.Api.Host.Internals;

internal static class EndpointExecutor
{
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

    (serviceInstance as IPlatformService)?.BindContext(services.GetService<PlatformAppService>());
    var feat = descriptor.StaticFeature ?? (serviceInstance as IPlatformService)?.Feature;

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
      descriptor.UsesRawHttp,
      descriptor.UsesFormFiles,
      serviceInstance,
      feat,
      httpRequest,
      services,
      ct);
  }

  public static async Task<IResult> ExecuteReflectedAsync(
    MethodInfo method,
    EndpointParameter[] parameters,
    Func<object, object?[], Task<IResult>> invoker,
    EndpointAuth auth,
    bool isAnonymous,
    bool usesRawHttp,
    bool usesFormFiles,
    object serviceInstance,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
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
          EndpointParameterKind.FormFiles => formCollection?.Files,
          EndpointParameterKind.RawJsonBody => rawBody,
          EndpointParameterKind.RouteOrBodyId => parsedId ?? (param.HasDefaultValue ? param.DefaultValue : 0L),
          EndpointParameterKind.MultipartBody => BindMultipartRequest(param.ParameterType, httpRequest, formCollection),
          _ => BindJsonOrService(param, rawBody, httpRequest, services)
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
      var result = await platformService.ExecuteDynamicEndpointAsync(
        descriptor.ActionName,
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

  private static object? BindMultipartRequest(Type parameterType, HttpRequest httpRequest, IFormCollection? form)
  {
    var instance = Activator.CreateInstance(parameterType)!;
    if (instance is IMultipartServiceRequest bindable && form is not null)
    {
      bindable.BindMultipart(httpRequest, form);
    }

    return instance;
  }

  private static object? BindJsonOrService(
    EndpointParameter param,
    JsonElement? rawBody,
    HttpRequest httpRequest,
    IServiceProvider services)
  {
    if (services.GetService(param.ParameterType) is { } resolvedService)
    {
      return resolvedService;
    }

    if (!param.ParameterType.IsClass ||
        param.ParameterType == typeof(string) ||
        IsUnregisteredFrameworkService(param.ParameterType))
    {
      return param.HasDefaultValue ? param.DefaultValue : null;
    }

    object? instance = null;
    if (typeof(IScopedServiceRequest).IsAssignableFrom(param.ParameterType) &&
        param.ParameterType.GetConstructor(Type.EmptyTypes) is not null)
    {
      var probe = (IScopedServiceRequest)Activator.CreateInstance(param.ParameterType)!;
      if (probe.HandlesOwnJsonDeserialization)
      {
        instance = probe;
      }
    }

    if (instance is null)
    {
      var json = rawBody.HasValue ? rawBody.Value.GetRawText() : "{}";
      instance = JsonSerializer.Deserialize(json, param.ParameterType, PlatformJsonOptions.DefaultOptions)
        ?? (param.ParameterType.GetConstructor(Type.EmptyTypes) is not null
          ? Activator.CreateInstance(param.ParameterType)
          : null);
    }

    if (instance is IScopedServiceRequest scoped)
    {
      if (rawBody.HasValue)
      {
        scoped.BindJsonBody(rawBody.Value);
      }

      ServiceRequestBinder.ApplyScopedHttpMetadata(httpRequest, scoped, rawBody);
    }

    return instance;
  }

  private static bool IsUnregisteredFrameworkService(Type parameterType) =>
    parameterType.IsAbstract ||
    parameterType == typeof(PlatformOptions) ||
    typeof(IPlatformService).IsAssignableFrom(parameterType);

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
