using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Host.Internals;

internal sealed class EndpointRegistry
{
  private static readonly ConcurrentDictionary<Type, IReadOnlyList<ReflectedEndpoint>> ReflectedTypeCache = new();

  public FrozenDictionary<string, EndpointDescriptor> ByRoute { get; }
  public FrozenDictionary<string, Type> RouteDbContextMap { get; }
  public IReadOnlyList<EndpointDescriptor> All { get; }

  private EndpointRegistry(
    List<EndpointDescriptor> descriptors,
    Dictionary<string, EndpointDescriptor> byRoute,
    Dictionary<string, Type> dbContextByRoute)
  {
    All = descriptors;
    ByRoute = byRoute.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    RouteDbContextMap = dbContextByRoute.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
  }

  public static EndpointRegistry Build(ModuleCatalog catalog)
  {
    var descriptors = new List<EndpointDescriptor>();
    var byRoute = new Dictionary<string, EndpointDescriptor>(StringComparer.OrdinalIgnoreCase);
    var dbContextByRoute = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

    foreach (var (serviceName, serviceType) in catalog.ServiceRoutes)
    {
      var exactFeat = catalog.FindFeature(serviceName);
      var feat = catalog.FindFeature(serviceType) ?? exactFeat;
      var dbContextType = ModuleCatalog.GetServiceDbContextType(serviceType);

      foreach (var reflected in DescribeServiceType(serviceType))
      {
        foreach (var actionName in reflected.ActionNames)
        {
          var pattern = $"/{serviceName}/{actionName}";
          if (byRoute.ContainsKey(pattern))
          {
            continue;
          }

          var desc = new EndpointDescriptor
          {
            RoutePattern = pattern,
            ServiceName = serviceName,
            ActionName = actionName,
            ServiceType = serviceType,
            DbContextType = dbContextType,
            StaticFeature = feat,
            Method = reflected.Method,
            Auth = reflected.Auth,
            IsAnonymous = reflected.IsAnonymous,
            UsesRawHttp = reflected.UsesRawHttp,
            UsesFormFiles = reflected.UsesFormFiles,
            Parameters = reflected.Parameters,
            CompiledInvoker = reflected.CompiledInvoker
          };
          descriptors.Add(desc);
          byRoute[pattern] = desc;
          if (dbContextType is not null)
          {
            dbContextByRoute[$"/api{pattern}"] = dbContextType;
          }
        }
      }

      if (OverridesDynamicEndpoints(serviceType) && catalog.CreateServiceInstance(serviceType) is IPlatformService probe)
      {
        foreach (var dynAction in probe.GetDynamicEndpointNames(catalog))
        {
          if (string.IsNullOrWhiteSpace(dynAction))
          {
            continue;
          }

          var pattern = $"/{serviceName}/{dynAction}";
          if (byRoute.ContainsKey(pattern))
          {
            continue;
          }

          var desc = new EndpointDescriptor
          {
            RoutePattern = pattern,
            ServiceName = serviceName,
            ActionName = dynAction,
            ServiceType = serviceType,
            DbContextType = dbContextType,
            StaticFeature = feat,
            Auth = EndpointAuth.Custom,
            IsDynamicAction = true
          };
          descriptors.Add(desc);
          byRoute[pattern] = desc;
          if (dbContextType is not null)
          {
            dbContextByRoute[$"/api{pattern}"] = dbContextType;
          }
        }
      }
    }

    return new EndpointRegistry(descriptors, byRoute, dbContextByRoute);
  }

  public static IReadOnlyList<ReflectedEndpoint> DescribeServiceType(Type serviceType) =>
    ReflectedTypeCache.GetOrAdd(serviceType, static t =>
    {
      var list = new List<ReflectedEndpoint>();
      foreach (var method in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
      {
        if (!ServiceEndpointsExtensions.IsEndpointMethod(method))
        {
          continue;
        }

        var endpointAttr = method.GetCustomAttribute<EndpointAttribute>(inherit: true);
        var directEndpointAttr = method.GetCustomAttribute<EndpointAttribute>(inherit: false);
        var hasExplicitGuard = method.GetCustomAttribute<RequireActionAttribute>(inherit: true) is not null ||
                               method.GetCustomAttribute<RequireRightAttribute>(inherit: true) is not null;
        var isAnon = method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null ||
                     method.GetCustomAttribute<WebhookEndpointAttribute>(inherit: true) is not null;

        var rawParams = method.GetParameters();
        var parameters = new EndpointParameter[rawParams.Length];
        var hasStructuredBody = false;
        var hasRawHttp = false;
        var hasFormFiles = false;

        for (var i = 0; i < rawParams.Length; i++)
        {
          var p = rawParams[i];
          var kind = ClassifyParameter(p.ParameterType);
          if (kind is EndpointParameterKind.HttpRequest or EndpointParameterKind.HttpContext)
          {
            hasRawHttp = true;
          }
          else if (kind == EndpointParameterKind.FormFiles)
          {
            hasFormFiles = true;
          }
          else if (kind is EndpointParameterKind.RawJsonBody or EndpointParameterKind.MultipartBody or EndpointParameterKind.JsonBodyOrService)
          {
            hasStructuredBody = true;
          }

          parameters[i] = new EndpointParameter(
            p.Name ?? $"arg{i}",
            p.ParameterType,
            kind,
            p.HasDefaultValue,
            p.HasDefaultValue ? p.DefaultValue : null);
        }

        list.Add(new ReflectedEndpoint
        {
          Method = method,
          ActionNames = ResolveActionNames(method, endpointAttr),
          Auth = directEndpointAttr?.Auth ?? (hasExplicitGuard ? EndpointAuth.Default : endpointAttr?.Auth ?? EndpointAuth.Default),
          IsAnonymous = isAnon,
          UsesRawHttp = hasRawHttp && !hasStructuredBody && !hasFormFiles,
          UsesFormFiles = hasFormFiles,
          Parameters = parameters,
          CompiledInvoker = EndpointInvokerCompiler.Compile(t, method)
        });
      }

      return list;
    });

  public static ReflectedEndpoint? FindReflectedEndpoint(Type serviceType, string method)
  {
    var normalized = StripAsyncSuffix(method.Replace("-", "", StringComparison.Ordinal));
    foreach (var ep in DescribeServiceType(serviceType))
    {
      if (ep.ActionNames.Contains(method, StringComparer.OrdinalIgnoreCase) ||
          string.Equals(ep.Method.Name, method, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(ep.Method.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(StripAsyncSuffix(ep.Method.Name), method, StringComparison.OrdinalIgnoreCase) ||
          string.Equals(StripAsyncSuffix(ep.Method.Name), normalized, StringComparison.OrdinalIgnoreCase))
      {
        return ep;
      }
    }

    return null;
  }

  private static EndpointParameterKind ClassifyParameter(Type parameterType)
  {
    if (parameterType == typeof(CancellationToken))
    {
      return EndpointParameterKind.CancellationToken;
    }

    if (parameterType == typeof(HttpRequest))
    {
      return EndpointParameterKind.HttpRequest;
    }

    if (parameterType == typeof(HttpContext))
    {
      return EndpointParameterKind.HttpContext;
    }

    if (parameterType == typeof(IServiceProvider))
    {
      return EndpointParameterKind.ServiceProvider;
    }

    if (parameterType == typeof(IFormFileCollection))
    {
      return EndpointParameterKind.FormFiles;
    }

    if (parameterType == typeof(JsonElement?))
    {
      return EndpointParameterKind.RawJsonBody;
    }

    if (parameterType == typeof(long))
    {
      return EndpointParameterKind.RouteOrBodyId;
    }

    if (typeof(IMultipartServiceRequest).IsAssignableFrom(parameterType))
    {
      return EndpointParameterKind.MultipartBody;
    }

    return EndpointParameterKind.JsonBodyOrService;
  }

  private static IReadOnlyList<string> ResolveActionNames(MethodInfo method, EndpointAttribute? endpointAttr)
  {
    if (!string.IsNullOrWhiteSpace(endpointAttr?.Name))
    {
      return [endpointAttr.Name.Trim('/')];
    }

    var stripped = StripAsyncSuffix(method.Name);
    var names = new List<string>(4);
    AddUnique(names, ServicePathResolver.ToKebabCase(stripped));
    AddUnique(names, ServicePathResolver.ToKebabCase(method.Name));
    AddUnique(names, stripped.ToLowerInvariant());
    AddUnique(names, method.Name.ToLowerInvariant());
    return names;
  }

  private static void AddUnique(List<string> list, string candidate)
  {
    if (!string.IsNullOrWhiteSpace(candidate) && !list.Contains(candidate, StringComparer.OrdinalIgnoreCase))
    {
      list.Add(candidate);
    }
  }

  private static string StripAsyncSuffix(string name) =>
    name.Length > "Async".Length && name.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
      ? name[..^"Async".Length]
      : name;

  private static bool OverridesDynamicEndpoints(Type serviceType)
  {
    for (var cur = serviceType; cur is not null && cur != typeof(object); cur = cur.BaseType)
    {
      foreach (var m in cur.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
      {
        if (m.GetParameters() is [{ ParameterType: var pt }] &&
            pt == typeof(ModuleCatalog) &&
            (m.Name == nameof(IPlatformService.GetDynamicEndpointNames) ||
             m.Name.EndsWith("." + nameof(IPlatformService.GetDynamicEndpointNames), StringComparison.Ordinal)))
        {
          return true;
        }
      }
    }

    return false;
  }
}
