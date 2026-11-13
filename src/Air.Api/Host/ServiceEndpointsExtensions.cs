using Microsoft.AspNetCore.Authorization;
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

    var registry = services.GetService<EndpointRegistry>();
    if (registry is not null && registry.RouteDbContextMap.TryGetValue(rawPath.TrimEnd('/'), out var mappedCtx))
    {
      services.GetService<DbScope>()?.Bind(mappedCtx);
      return;
    }

    var servicePath = rawPath["/api/".Length..].Trim('/');
    var lastSlash = servicePath.LastIndexOf('/');
    if (lastSlash <= 0)
    {
      return;
    }

    var serviceName = servicePath[..lastSlash];
    var serviceType = catalog.FindServiceType(serviceName)
      ?? catalog.FindFeature(serviceName)?.ServiceType;

    if (serviceType is not null)
    {
      services.GetService<DbScope>()?.Bind(ModuleCatalog.GetServiceDbContextType(serviceType));
    }
  }

  public static void MapServiceEndpoints(this RouteGroupBuilder api)
  {
    var rootServices = ((IEndpointRouteBuilder)api).ServiceProvider;
    var registry = rootServices.GetService<EndpointRegistry>()
      ?? EndpointRegistry.Build(rootServices.GetRequiredService<ModuleCatalog>());

    foreach (var descriptor in registry.All)
    {
      var captured = descriptor;
      var builder = api.MapPost(captured.RoutePattern, async (
        HttpRequest httpRequest,
        IServiceProvider services,
        CancellationToken ct) =>
        await PlatformEndpointsExtensions.WithRequestCultureAsync(
          httpRequest,
          _ => EndpointExecutor.ExecuteAsync(captured, httpRequest, services, ct)));

      if (captured.IsAnonymous)
      {
        builder.AllowAnonymous();
      }
    }
  }

  public static async Task<IResult> DispatchEndpointAsync(
    Type serviceType,
    object serviceInstance,
    string method,
    Feature? feat,
    HttpRequest httpRequest,
    IServiceProvider services,
    CancellationToken ct)
  {
    (serviceInstance as IPlatformService)?.BindContext(services.GetService<PlatformAppService>());
    if (EndpointRegistry.FindReflectedEndpoint(serviceType, method) is { } reflected)
    {
      return await EndpointExecutor.ExecuteReflectedAsync(
        reflected.Method,
        reflected.Parameters,
        reflected.CompiledInvoker,
        reflected.Auth,
        reflected.IsAnonymous,
        reflected.UsesRawHttp,
        reflected.UsesFormFiles,
        serviceInstance,
        feat ?? (serviceInstance as IPlatformService)?.Feature,
        httpRequest,
        services,
        ct);
    }

    if (serviceInstance is IPlatformService platformService)
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
        var dyn = await platformService.ExecuteDynamicEndpointAsync(method, httpRequest, body, services, ct);
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
    if (actionMethod.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null ||
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

      if (string.Equals(reqAttr.ActionName, Right.View, StringComparison.OrdinalIgnoreCase))
      {
        return CanView(targetFeat, userContext);
      }

      if (string.Equals(reqAttr.ActionName, Right.Edit, StringComparison.OrdinalIgnoreCase))
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

      if (string.Equals(reqAction.ActionName, Right.View, StringComparison.OrdinalIgnoreCase))
      {
        return CanView(feat, userContext);
      }

      if (string.Equals(reqAction.ActionName, Right.Edit, StringComparison.OrdinalIgnoreCase))
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

  public static bool IsEndpointMethod(MethodInfo m) =>
    !m.IsSpecialName &&
    !m.IsGenericMethodDefinition &&
    m.DeclaringType != null &&
    (m.GetCustomAttribute<EndpointAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<RequireActionAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<RequireRightAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<WebhookEndpointAttribute>(inherit: true) != null ||
     m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) != null);
}
