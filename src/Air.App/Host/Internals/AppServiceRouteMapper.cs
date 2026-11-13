using Platform.Api.Host.Internals;

namespace Platform.App.Host.Internals;

internal static class AppServiceRouteMapper
{
  private static readonly string[] BuiltInGridMethods =
  [
    "get-data",
    "get-count",
    "get-one",
    "delete",
    "save-cell",
    "export",
    "get-action-input",
    "move-status"
  ];

  private static readonly string[] BuiltInFormMethods =
  [
    "get-data",
    "save",
    "get-action-input"
  ];

  public static void MapServiceEndpoints(
    IServiceEndpointDispatcher dispatcher,
    RouteGroupBuilder api,
    ModuleCatalog catalog,
    Type serviceType,
    string serviceName,
    Feature? feature)
  {
    var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (typeof(IGridService).IsAssignableFrom(serviceType))
    {
      foreach (var method in BuiltInGridMethods)
      {
        MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, method, feature);
      }

      MapSubMethodRoute(dispatcher, api, mapped, serviceType, serviceName, "get-action-input", feature);
      MapSubMethodRoute(dispatcher, api, mapped, serviceType, serviceName, "move-status", feature);

      if (serviceType.GetMethod("Upload") is not null)
      {
        MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, "upload", feature);
      }

      MapCustomCallableRoutes(dispatcher, api, mapped, serviceType, serviceName, feature);

      if (catalog.CreateServiceInstance(serviceType) is IGridService gridProbe)
      {
        foreach (var actionRoute in gridProbe.GetRoutableActionMethods(catalog))
        {
          MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, actionRoute, feature);
        }
      }

      return;
    }

    if (typeof(IFormService).IsAssignableFrom(serviceType))
    {
      foreach (var method in BuiltInFormMethods)
      {
        MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, method, feature);
      }

      MapSubMethodRoute(dispatcher, api, mapped, serviceType, serviceName, "get-action-input", feature);
      MapSubMethodRoute(dispatcher, api, mapped, serviceType, serviceName, "move-status", feature);

      MapCustomCallableRoutes(dispatcher, api, mapped, serviceType, serviceName, feature);

      if (catalog.CreateServiceInstance(serviceType) is IFormService formProbe)
      {
        foreach (var actionRoute in formProbe.GetRoutableActionMethods(catalog))
        {
          MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, actionRoute, feature);
        }
      }

      return;
    }

    if (typeof(IReportService).IsAssignableFrom(serviceType) ||
        typeof(IScreenService).IsAssignableFrom(serviceType))
    {
      MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, "get-data", feature);
      MapCustomCallableRoutes(dispatcher, api, mapped, serviceType, serviceName, feature);
    }
  }

  private static void MapCustomCallableRoutes(
    IServiceEndpointDispatcher dispatcher,
    RouteGroupBuilder api,
    HashSet<string> mapped,
    Type serviceType,
    string serviceName,
    Feature? feature)
  {
    foreach (var actionMethod in ServiceRequestBinder.GetCallableActionMethods(serviceType))
    {
      var allowAnonymous = ServiceRequestBinder.IsAnonymousCallableMethod(actionMethod);
      foreach (var routeAction in ServiceRequestBinder.GetRouteActionNames(actionMethod))
      {
        MapFixedRoute(dispatcher, api, mapped, serviceType, serviceName, routeAction, feature, allowAnonymous);
      }
    }
  }

  private static void MapFixedRoute(
    IServiceEndpointDispatcher dispatcher,
    RouteGroupBuilder api,
    HashSet<string> mapped,
    Type serviceType,
    string serviceName,
    string method,
    Feature? feature,
    bool allowAnonymous = false)
  {
    var pattern = $"/{serviceName}/{method}";
    if (!mapped.Add(pattern))
    {
      return;
    }

    var builder = api.MapPost(pattern, async (
      HttpRequest httpRequest,
      IServiceProvider services,
      CancellationToken ct) =>
      await PlatformEndpointsExtensions.WithRequestCultureAsync(httpRequest, async _ =>
      {
        Localizer.Instance.EnsureManagersLoaded();
        return await dispatcher.DispatchAsync(serviceType, serviceName, method, null, feature, httpRequest, services, ct);
      }));

    if (allowAnonymous)
    {
      builder.AllowAnonymous();
    }
  }

  private static void MapSubMethodRoute(
    IServiceEndpointDispatcher dispatcher,
    RouteGroupBuilder api,
    HashSet<string> mapped,
    Type serviceType,
    string serviceName,
    string method,
    Feature? feature)
  {
    var pattern = $"/{serviceName}/{method}/{{subMethod}}";
    if (!mapped.Add(pattern))
    {
      return;
    }

    api.MapPost(pattern, async (
      string subMethod,
      HttpRequest httpRequest,
      IServiceProvider services,
      CancellationToken ct) =>
      await PlatformEndpointsExtensions.WithRequestCultureAsync(httpRequest, async _ =>
      {
        Localizer.Instance.EnsureManagersLoaded();
        return await dispatcher.DispatchAsync(serviceType, serviceName, method, subMethod, feature, httpRequest, services, ct);
      }));
  }

  internal static void AddFeatureCustomRightRoutes(HashSet<string> routes, Feature? feature)
  {
    if (feature is null)
    {
      return;
    }

    foreach (var right in feature.Rights.Values)
    {
      if (!right.IsPattern && !Right.StandardActions.Contains(right.Action))
      {
        routes.Add(ServicePathResolver.ToKebabCase(right.Action));
        routes.Add(right.Action.ToLowerInvariant());
      }
    }
  }
}
