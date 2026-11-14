using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        var pattern = $"/{serviceName}/{reflected.ActionName}";
        if (byRoute.ContainsKey(pattern))
        {
          continue;
        }

        var desc = new EndpointDescriptor
        {
          RoutePattern = pattern,
          ServiceName = serviceName,
          ActionName = reflected.ActionName,
          ServiceType = serviceType,
          DbContextType = dbContextType,
          StaticFeature = feat,
          Method = reflected.Method,
          Auth = reflected.Auth,
          IsAnonymous = reflected.IsAnonymous,
          RequireClientHeader = reflected.RequireClientHeader,
          UsesRawHttp = reflected.UsesRawHttp,
          UsesFormFiles = reflected.UsesFormFiles,
          ExplicitRequestType = reflected.ExplicitRequestType,
          ExplicitResponseType = reflected.ExplicitResponseType,
          ExplicitResponseNullable = reflected.ExplicitResponseNullable,
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
      var nullability = new NullabilityInfoContext();
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
          var (kind, factory) = ClassifyParameter(p, method);
          if (kind is EndpointParameterKind.HttpRequest or EndpointParameterKind.HttpContext)
          {
            hasRawHttp = true;
          }
          else if (kind == EndpointParameterKind.FormFiles)
          {
            hasFormFiles = true;
          }
          else if (IsBodyParameterKind(kind))
          {
            hasStructuredBody = true;
          }

          var isNullable = Nullable.GetUnderlyingType(p.ParameterType) is not null ||
                           nullability.Create(p).ReadState == NullabilityState.Nullable;

          parameters[i] = new EndpointParameter(
            p.Name ?? $"arg{i}",
            p.ParameterType,
            kind,
            isNullable,
            p.HasDefaultValue,
            p.HasDefaultValue ? p.DefaultValue : null,
            factory);
        }

        list.Add(new ReflectedEndpoint
        {
          Method = method,
          ActionName = ResolveActionName(method, endpointAttr),
          Auth = directEndpointAttr?.Auth ?? (hasExplicitGuard ? EndpointAuth.Default : endpointAttr?.Auth ?? EndpointAuth.Default),
          IsAnonymous = isAnon,
          RequireClientHeader = directEndpointAttr?.RequireClientHeader == true || endpointAttr?.RequireClientHeader == true,
          UsesRawHttp = hasRawHttp && !hasStructuredBody && !hasFormFiles,
          UsesFormFiles = hasFormFiles,
          ExplicitRequestType = directEndpointAttr?.RequestType ?? endpointAttr?.RequestType,
          ExplicitResponseType = directEndpointAttr?.ResponseType ?? endpointAttr?.ResponseType,
          ExplicitResponseNullable = directEndpointAttr?.ResponseNullable ?? endpointAttr?.ResponseNullable ?? false,
          Parameters = parameters,
          CompiledInvoker = t.ContainsGenericParameters ? null : EndpointInvokerCompiler.Compile(t, method)
        });
      }

      return list;
    });

  public static ReflectedEndpoint? FindReflectedEndpoint(Type serviceType, string actionName)
  {
    var trimmed = actionName.Trim('/');
    foreach (var ep in DescribeServiceType(serviceType))
    {
      if (string.Equals(ep.ActionName, trimmed, StringComparison.OrdinalIgnoreCase))
      {
        return ep;
      }
    }

    return null;
  }

  internal static bool IsBodyParameterKind(EndpointParameterKind kind) =>
    kind is EndpointParameterKind.RawJsonBody
      or EndpointParameterKind.MultipartBody
      or EndpointParameterKind.CustomScopedRequest
      or EndpointParameterKind.ScopedJsonBody
      or EndpointParameterKind.JsonBody;

  private static (EndpointParameterKind Kind, Func<object>? Factory) ClassifyParameter(ParameterInfo parameter, MethodInfo method)
  {
    var parameterType = parameter.ParameterType;
    if (parameterType == typeof(CancellationToken))
    {
      return (EndpointParameterKind.CancellationToken, null);
    }

    if (parameterType == typeof(HttpRequest))
    {
      return (EndpointParameterKind.HttpRequest, null);
    }

    if (parameterType == typeof(HttpContext))
    {
      return (EndpointParameterKind.HttpContext, null);
    }

    if (parameterType == typeof(IServiceProvider))
    {
      return (EndpointParameterKind.ServiceProvider, null);
    }

    if (parameterType == typeof(IFormFileCollection))
    {
      return (EndpointParameterKind.FormFiles, null);
    }

    if (parameterType == typeof(JsonElement?))
    {
      return (EndpointParameterKind.RawJsonBody, null);
    }

    if (parameterType == typeof(long) || parameterType == typeof(long?))
    {
      return (EndpointParameterKind.RouteOrBodyId, null);
    }

    if (typeof(IMultipartServiceRequest).IsAssignableFrom(parameterType))
    {
      return (EndpointParameterKind.MultipartBody, CompileParameterlessFactory(parameterType));
    }

    if (parameter.GetCustomAttribute<FromServicesAttribute>() is not null ||
        parameterType.IsInterface ||
        parameterType.IsAbstract ||
        typeof(IPlatformService).IsAssignableFrom(parameterType) ||
        parameterType == typeof(PlatformOptions) ||
        parameterType == typeof(TimeProvider))
    {
      return (EndpointParameterKind.FromServices, null);
    }

    if (!parameterType.IsClass ||
        parameterType == typeof(string) ||
        parameterType == typeof(object) ||
        typeof(Delegate).IsAssignableFrom(parameterType))
    {
      throw new InvalidOperationException(
        $"Unsupported [Endpoint] parameter '{parameter.Name}' of type '{parameterType.FullName}' on '{method.DeclaringType?.FullName}.{method.Name}'. Endpoint parameters must be a request DTO class/record, 'long'/'long?' id, framework HTTP/CancellationToken type, interface, or marked with [FromServices].");
    }

    var factory = CompileParameterlessFactory(parameterType);
    if (typeof(IScopedServiceRequest).IsAssignableFrom(parameterType))
    {
      var handlesCustom = factory is not null && ((IScopedServiceRequest)factory()).HandlesOwnJsonDeserialization;
      return (handlesCustom ? EndpointParameterKind.CustomScopedRequest : EndpointParameterKind.ScopedJsonBody, factory);
    }

    return (EndpointParameterKind.JsonBody, factory);
  }

  private static Func<object>? CompileParameterlessFactory(Type type)
  {
    if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters)
    {
      return null;
    }

    var ctor = type.GetConstructor(Type.EmptyTypes);
    return ctor is null
      ? null
      : Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
  }

  private static string ResolveActionName(MethodInfo method, EndpointAttribute? endpointAttr) =>
    !string.IsNullOrWhiteSpace(endpointAttr?.Name)
      ? endpointAttr.Name.Trim('/')
      : ServicePathResolver.ToKebabCase(StripAsyncSuffix(method.Name));

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
