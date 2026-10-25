namespace Platform.Api.Host;

public static class ServiceEndpointsExtensions
{
  private static readonly HashSet<string> ExcludedBaseTypeNames = new(StringComparer.Ordinal)
  {
    nameof(Object),
    nameof(IPlatformService),
    nameof(PlatformService),
    "IGridService",
    "GridService`1",
    "GridService`2",
    "IFormService",
    "FormService`1",
    "FormService`2",
    "IReportService",
    "ReportService",
    "IScreenService",
    "ScreenService"
  };

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

        var feat = catalog.FindFeature(serviceName)
          ?? (parts.Length >= 2 ? catalog.FindFeature(parts[0] + "/" + parts[1]) : null);

        var serviceType = feat?.ServiceType
          ?? catalog.FindServiceType(serviceName)
          ?? (parts.Length >= 2 ? catalog.FindServiceType(parts[0] + "/" + parts[1]) : null);

        if (serviceType is null)
        {
          return Results.NotFound($"Service '{serviceName}' not found.");
        }

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

    var actionMethod = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
      .Where(m => !m.IsSpecialName && m.DeclaringType != null && !ExcludedBaseTypeNames.Contains(m.DeclaringType.Name))
      .FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(m.Name, method.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
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
    if (!usesRawHttpRequest && httpRequest.ContentLength > 0 && !httpRequest.HasFormContentType)
    {
      try
      {
        using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
        rawBody = doc.RootElement.Clone();
      }
      catch { }
    }

    long? parsedId = null;
    if (httpRequest.Query.TryGetValue("id", out var qIdStr) && long.TryParse(qIdStr, out var qId))
    {
      parsedId = qId;
    }
    else if (rawBody is { ValueKind: JsonValueKind.Object } rb && rb.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var bodyId))
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
        args[i] = rawBody.HasValue
          ? JsonSerializer.Deserialize(rawBody.Value.GetRawText(), pType, PlatformJsonOptions.DefaultOptions) ?? Activator.CreateInstance(pType)
          : Activator.CreateInstance(pType);
      }
    }

    return await InvokeServiceMethodAsync(actionMethod, serviceInstance, args);
  }

  public static bool CanView(Feature? feat, IUserContext? userContext)
  {
    if (userContext is null or { IsAuthenticated: false })
    {
      return false;
    }

    if (feat == null || string.IsNullOrEmpty(feat.Module))
    {
      return true;
    }

    return userContext.Can(feat.View) ||
           userContext.Can(feat.MenuView) ||
           userContext.Can(feat.Edit) ||
           userContext.Can(feat.All) ||
           userContext.Can("*");
  }

  public static bool CanExport(Feature? feat, IUserContext? userContext)
  {
    if (userContext is null or { IsAuthenticated: false })
    {
      return false;
    }

    if (feat == null || string.IsNullOrEmpty(feat.Module))
    {
      return true;
    }

    return userContext.Can(feat.Export) ||
           userContext.Can(feat.View) ||
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
      return Feature.FeaturesByServiceType.TryGetValue(reqAttr.Service, out var targetFeat) &&
             (userContext.Can(targetFeat.Action(reqAttr.ActionName)) ||
              userContext.Can(targetFeat.All) ||
              userContext.Can("*"));
    }

    var reqAction = actionMethod.GetCustomAttribute<RequireActionAttribute>(inherit: true);
    if (reqAction != null)
    {
      if (string.Equals(reqAction.ActionName, nameof(EndpointAction.View), StringComparison.OrdinalIgnoreCase))
      {
        return CanView(feat, userContext);
      }

      if (string.Equals(reqAction.ActionName, nameof(EndpointAction.Restore), StringComparison.OrdinalIgnoreCase))
      {
        return feat != null && (userContext.Can(feat.Restore) || userContext.Can(feat.All)) ||
               userContext.Can("*") ||
               actionMethod.DeclaringType?.Name == "ChangeGridService" &&
                userContext.Permissions.Any(p =>
                  p.EndsWith(".Restore", StringComparison.OrdinalIgnoreCase) ||
                  p.EndsWith(".*", StringComparison.Ordinal));
      }

      if (reqAction.FallbackToView && (feat == null || !feat.HasAction(reqAction.ActionName)))
      {
        return CanView(feat, userContext);
      }

      if (feat != null)
      {
        return userContext.Can(feat.Action(reqAction.ActionName)) ||
               userContext.Can(feat.All) ||
               userContext.Can("*");
      }
    }

    var targetKey = feat?.Edit?.Key;
    return targetKey != null
      ? userContext.Can(targetKey) || userContext.Can(feat!.All) || userContext.Can("*")
      : userContext.Can("*");
  }

  public static async Task<IResult> InvokeServiceMethodAsync(MethodInfo method, object target, object?[] args)
  {
    var resultTask = method.Invoke(target, args);
    if (resultTask is Task<IResult> taskResult)
    {
      return await taskResult;
    }

    if (resultTask is Task task)
    {
      await task.ConfigureAwait(false);
      var result = task.GetType().GetProperty("Result")?.GetValue(task);
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
}
