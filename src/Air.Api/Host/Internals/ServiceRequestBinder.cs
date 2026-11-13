using Microsoft.AspNetCore.Authorization;

namespace Platform.Api.Host.Internals;

internal static class ServiceRequestBinder
{
  private static readonly ConcurrentDictionary<Type, IReadOnlyList<MethodInfo>> CallableMethodsCache = new();

  public static IReadOnlyList<MethodInfo> GetCallableActionMethods(Type serviceType) =>
    CallableMethodsCache.GetOrAdd(
      serviceType,
      static t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(ServiceEndpointsExtensions.IsCallableServiceAction)
        .ToList());

  public static IReadOnlyList<string> GetRouteActionNames(MethodInfo method)
  {
    var stripped = StripAsyncSuffix(method.Name);
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
      ServicePathResolver.ToKebabCase(stripped),
      ServicePathResolver.ToKebabCase(method.Name),
      stripped.ToLowerInvariant(),
      method.Name.ToLowerInvariant()
    };
    names.RemoveWhere(string.IsNullOrWhiteSpace);
    return [.. names];
  }

  public static bool IsAnonymousCallableMethod(MethodInfo method) =>
    method.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null ||
    method.GetCustomAttribute<WebhookEndpointAttribute>(inherit: true) is not null;

  public static MethodInfo? ResolveCallableActionMethod(Type serviceType, string method)
  {
    var methods = GetCallableActionMethods(serviceType);

    var normalized = StripAsyncSuffix(method.Replace("-", "", StringComparison.Ordinal));
    return methods.FirstOrDefault(m =>
      string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(m.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(StripAsyncSuffix(m.Name), method, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(StripAsyncSuffix(m.Name), normalized, StringComparison.OrdinalIgnoreCase));
  }

  private static string StripAsyncSuffix(string name) =>
    name.Length > "Async".Length && name.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
      ? name[..^"Async".Length]
      : name;

  public static void ApplyScopedQueryParams(HttpRequest httpRequest, IScopedServiceRequest req)
  {
    if (req.Id is null or <= 0 &&
        httpRequest.Query.TryGetValue("id", out var idStr) &&
        long.TryParse(idStr, out var idVal))
    {
      req.Id = idVal;
    }

    if (req.ParentId is null &&
        httpRequest.Query.TryGetValue("parentId", out var pidStr) &&
        long.TryParse(pidStr, out var pidVal))
    {
      req.ParentId = pidVal;
    }

    if (string.IsNullOrEmpty(req.ParentService) &&
        httpRequest.Query.TryGetValue("parentService", out var psStr))
    {
      req.ParentService = psStr;
    }
  }

  public static async Task<(JsonElement? Body, bool IsMalformed)> TryReadJsonBodyAsync(
    HttpRequest httpRequest,
    CancellationToken ct)
  {
    if (!ServiceEndpointsExtensions.HasJsonBody(httpRequest) || httpRequest.HasFormContentType)
    {
      return (null, false);
    }

    try
    {
      using var doc = await JsonDocument.ParseAsync(httpRequest.Body, cancellationToken: ct);
      return (doc.RootElement.Clone(), false);
    }
    catch (JsonException)
    {
      return (null, true);
    }
  }
}
