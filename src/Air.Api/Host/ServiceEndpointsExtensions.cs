using Platform.Api.Host.Internals;

namespace Platform.Api.Host;

public static class ServiceEndpointsExtensions
{
  internal static void TryBindModuleDbScope(PathString requestPath, ModuleCatalog catalog, IServiceProvider services)
  {
    var rawPath = requestPath.Value;
    if (string.IsNullOrEmpty(rawPath) || !rawPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    var servicePath = rawPath["/api/".Length..].Trim('/');
    if (string.IsNullOrEmpty(servicePath) ||
        servicePath.StartsWith("auth/", StringComparison.OrdinalIgnoreCase) ||
        servicePath.StartsWith("platform-", StringComparison.OrdinalIgnoreCase) ||
        servicePath.StartsWith("metadata", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    var parts = servicePath.Split('/');
    if (parts.Length < 2)
    {
      return;
    }

    var serviceName = parts.Length >= 3 &&
                      (string.Equals(parts[^2], "move-status", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(parts[^2], "get-action-input", StringComparison.OrdinalIgnoreCase))
      ? string.Join('/', parts[..^2])
      : string.Join('/', parts[..^1]);

    var serviceType = catalog.FindServiceType(serviceName)
      ?? catalog.FindFeature(serviceName)?.ServiceType;

    if (serviceType is not null)
    {
      services.GetService<DbScope>()?.Bind(ModuleCatalog.GetServiceDbContextType(serviceType));
    }
  }

  public static void MapServiceEndpoints(this RouteGroupBuilder api)
  {
    api.MapPost("/{**servicePath}", async (
      string servicePath,
      HttpRequest httpRequest,
      ModuleCatalog catalog,
      IServiceProvider services,
      CancellationToken ct) =>
      await PlatformEndpointsExtensions.WithRequestCultureAsync(httpRequest, async _ =>
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
        else if (parts.Length >= 3 && string.Equals(parts[^2], "get-action-input", StringComparison.OrdinalIgnoreCase))
        {
          serviceName = string.Join('/', parts[..^2]);
          method = "get-action-input";
          subMethod = parts[^1];
        }
        else
        {
          serviceName = string.Join('/', parts[..^1]);
          method = parts[^1];
        }

        var exactFeat = catalog.FindFeature(serviceName);
        var serviceType = catalog.FindServiceType(serviceName) ?? exactFeat?.ServiceType;
        if (serviceType is null)
        {
          return Results.NotFound($"Service '{serviceName}' not found.");
        }

        var feat = catalog.FindFeature(serviceType) ?? exactFeat;

        services.GetService<DbScope>()?.Bind(ModuleCatalog.GetServiceDbContextType(serviceType));

        foreach (var dispatcher in services.GetServices<IServiceEndpointDispatcher>())
        {
          if (dispatcher.CanDispatch(serviceType))
          {
            return await dispatcher.DispatchAsync(serviceType, serviceName, method, subMethod, feat, httpRequest, services, ct);
          }
        }

        var customService = services.GetService(serviceType);
        if (customService is not null)
        {
          if (customService is IPlatformService ps)
          {
            feat ??= ps.Feature;
          }

          return await DispatchCustomServiceMethodAsync(serviceType, customService, method, feat, httpRequest, services, ct);
        }

        return Results.NotFound();
      }));
  }

  public static async Task<IResult> DispatchCustomServiceMethodAsync(
    Type serviceType,
    object serviceInstance,
    string method,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct,
    Func<Type, HttpRequest, JsonElement?, object?>? customParameterBinder = null)
  {
    if (serviceInstance is IPlatformService ps && services.GetService<PlatformAppService>() is { } appInstance)
    {
      ps.AttachPlatformApp(appInstance);
    }

    var actionMethod = ServiceRequestBinder.ResolveCallableActionMethod(serviceType, method);
    if (actionMethod == null)
    {
      return Results.NotFound();
    }

    var isAnonymousOrWebhook =
      actionMethod.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(inherit: true) != null ||
      actionMethod.GetCustomAttribute<WebhookEndpointAttribute>(inherit: true) != null;

    var userContext = services.GetService<IUserContext>();
    if (!isAnonymousOrWebhook && userContext is null or { IsAuthenticated: false })
    {
      return Results.Unauthorized();
    }

    if (!IsCustomActionPermitted(actionMethod, feat, userContext))
    {
      return Results.UnprocessableEntity(new MutationResultDto
      {
        Outcome = MutationOutcome.Refused,
        Title = Localizer.Instance["MutationRefused"],
        Messages = [new MessageDto { Detail = Localizer.Instance["MutationRefused"], Type = MessageType.Error }]
      });
    }

    var parameters = actionMethod.GetParameters();
    var usesRawHttpRequest = parameters.Any(p => p.ParameterType == typeof(HttpRequest) || p.ParameterType == typeof(HttpContext));

    JsonElement? rawBody = null;
    if (!usesRawHttpRequest)
    {
      var (parsedBody, isMalformed) = await ServiceRequestBinder.TryReadJsonBodyAsync(httpRequest, ct);
      if (isMalformed)
      {
        return Results.BadRequest(new { detail = "Malformed JSON request body." });
      }

      rawBody = parsedBody;
    }

    long? parsedId = null;
    if (httpRequest.Query.TryGetValue("id", out var qIdStr) && long.TryParse(qIdStr, out var qId))
    {
      parsedId = qId;
    }
    else if (rawBody is { ValueKind: JsonValueKind.Object } rb && TryReadBodyId(rb, out var bodyId))
    {
      parsedId = bodyId;
    }

    var args = new object?[parameters.Length];
    for (var i = 0; i < parameters.Length; i++)
    {
      var pType = parameters[i].ParameterType;
      if (pType == typeof(CancellationToken))
      {
        args[i] = ct;
      }
      else if (pType == typeof(HttpRequest))
      {
        args[i] = httpRequest;
      }
      else if (pType == typeof(HttpContext))
      {
        args[i] = httpRequest.HttpContext;
      }
      else if (pType == typeof(IServiceProvider))
      {
        args[i] = services;
      }
      else if (customParameterBinder?.Invoke(pType, httpRequest, rawBody) is { } boundCustom)
      {
        args[i] = boundCustom;
      }
      else if (pType == typeof(long) && parsedId.HasValue)
      {
        args[i] = parsedId.Value;
      }
      else if (services.GetService(pType) is { } resolvedService)
      {
        args[i] = resolvedService;
      }
      else if (pType.IsClass && pType != typeof(string))
      {
        args[i] = DeserializeOrCreateRequest(pType, rawBody);
      }
    }

    return await InvokeServiceMethodAsync(actionMethod, serviceInstance, args);
  }

  private static bool TryReadBodyId(JsonElement body, out long id)
  {
    foreach (var prop in body.EnumerateObject())
    {
      if (string.Equals(prop.Name, "id", StringComparison.OrdinalIgnoreCase) &&
          prop.Value.ValueKind == JsonValueKind.Number &&
          prop.Value.TryGetInt64(out id))
      {
        return true;
      }
    }

    id = 0;
    return false;
  }

  private static object? DeserializeOrCreateRequest(Type requestType, JsonElement? rawBody)
  {
    var json = rawBody.HasValue ? rawBody.Value.GetRawText() : "{}";
    var deserialized = JsonSerializer.Deserialize(json, requestType, PlatformJsonOptions.DefaultOptions);
    if (deserialized is not null)
    {
      return deserialized;
    }

    return requestType.GetConstructor(Type.EmptyTypes) is not null
      ? Activator.CreateInstance(requestType)
      : null;
  }

  public static bool HasJsonBody(HttpRequest httpRequest) =>
    httpRequest.ContentLength is > 0 ||
    httpRequest.ContentLength is null &&
    (httpRequest.HasJsonContentType() || httpRequest.Body.CanSeek && httpRequest.Body.Length > 0);

  public static bool CanView(Feature? feat, IUserContext? userContext)
  {
    if (userContext is null or { IsAuthenticated: false } ||
        feat == null ||
        string.IsNullOrEmpty(feat.Module))
    {
      return false;
    }

    return userContext.Can(feat.View) ||
           userContext.Can(feat.Edit);
  }

  public static bool CanEdit(Feature? feat, IUserContext? userContext)
  {
    if (userContext is null or { IsAuthenticated: false } ||
        feat == null ||
        string.IsNullOrEmpty(feat.Module) ||
        feat.IsReadOnly)
    {
      return false;
    }

    return userContext.Can(feat.Edit) ||
           userContext.Can(feat.All) ||
           userContext.Can("*");
  }

  public static bool CanExport(Feature? feat, IUserContext? userContext)
  {
    if (userContext is null or { IsAuthenticated: false } ||
        feat == null ||
        string.IsNullOrEmpty(feat.Module) ||
        !feat.HasExport)
    {
      return false;
    }

    return userContext.Can(feat.Export) ||
           userContext.Can(feat.All) ||
           userContext.Can("*");
  }

  public static bool IsCustomActionPermitted(MethodInfo actionMethod, Feature? feat, IUserContext? userContext)
  {
    if (actionMethod.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(inherit: true) != null ||
        actionMethod.GetCustomAttribute<WebhookEndpointAttribute>(inherit: true) != null)
    {
      return true;
    }

    if (userContext is null or { IsAuthenticated: false })
    {
      return false;
    }

    var reqAttr = actionMethod.GetCustomAttribute<RequireRightAttribute>(inherit: true);
    if (reqAttr != null)
    {
      var targetFeat = ModuleCatalog.Instance.FindFeature(reqAttr.Service);
      if (targetFeat == null)
      {
        return false;
      }

      if (string.Equals(reqAttr.ActionName, nameof(EndpointAction.View), StringComparison.OrdinalIgnoreCase))
      {
        return CanView(targetFeat, userContext);
      }

      if (string.Equals(reqAttr.ActionName, nameof(EndpointAction.Edit), StringComparison.OrdinalIgnoreCase))
      {
        return CanEdit(targetFeat, userContext);
      }

      return targetFeat.TryGetRight(reqAttr.ActionName, out var targetRight) &&
             (userContext.Can(targetRight) ||
              userContext.Can(targetFeat.All) ||
              userContext.Can("*"));
    }

    var reqAction = actionMethod.GetCustomAttribute<RequireActionAttribute>(inherit: true);
    if (reqAction != null)
    {
      if (feat == null || string.IsNullOrEmpty(feat.Module))
      {
        return false;
      }

      if (string.Equals(reqAction.ActionName, nameof(EndpointAction.View), StringComparison.OrdinalIgnoreCase))
      {
        return CanView(feat, userContext);
      }

      if (string.Equals(reqAction.ActionName, nameof(EndpointAction.Edit), StringComparison.OrdinalIgnoreCase))
      {
        return CanEdit(feat, userContext);
      }

      if (reqAction.FallbackToView && !feat.HasAction(reqAction.ActionName))
      {
        return CanView(feat, userContext);
      }

      return feat.TryGetRight(reqAction.ActionName, out var actionRight) &&
             (userContext.Can(actionRight) ||
              userContext.Can(feat.All) ||
              userContext.Can("*"));
    }

    return feat is { IsReadOnly: true }
      ? CanView(feat, userContext)
      : CanEdit(feat, userContext);
  }

  public static async Task<IResult> InvokeServiceMethodAsync(MethodInfo method, object target, object?[] args)
  {
    object? resultTask;
    try
    {
      resultTask = method.Invoke(target, args);
    }
    catch (TargetInvocationException ex) when (ex.InnerException is not null)
    {
      System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
      return Results.Empty;
    }

    if (resultTask is Task<IResult> taskResult)
    {
      return await taskResult;
    }

    if (resultTask is Task task)
    {
      await task.ConfigureAwait(false);
      var taskType = task.GetType();
      var result = taskType.IsGenericType ? taskType.GetProperty("Result")?.GetValue(task) : null;
      if (result is IResult r)
      {
        return r;
      }

      return PlatformEndpointsExtensions.ToMutationResult(result ?? new MutationResultDto { Outcome = MutationOutcome.Committed });
    }

    if (resultTask is IResult immediateResult)
    {
      return immediateResult;
    }

    return PlatformEndpointsExtensions.ToMutationResult(resultTask ?? new MutationResultDto { Outcome = MutationOutcome.Committed });
  }

  public static bool IsCallableServiceAction(MethodInfo m) =>
    !m.IsSpecialName &&
    !m.IsGenericMethodDefinition &&
    m.DeclaringType != null &&
    (m.GetCustomAttribute<ServiceActionAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<RequireActionAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<RequireRightAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<WebhookEndpointAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(inherit: true) != null);
}
