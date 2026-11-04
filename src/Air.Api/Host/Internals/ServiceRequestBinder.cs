namespace Platform.Api.Host.Internals;

internal static class ServiceRequestBinder
{
  private static readonly ConcurrentDictionary<Type, IReadOnlyList<MethodInfo>> CallableMethodsCache = new();

  public static MethodInfo? ResolveCallableActionMethod(Type serviceType, string method)
  {
    var methods = CallableMethodsCache.GetOrAdd(
      serviceType,
      static t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(ServiceEndpointsExtensions.IsCallableServiceAction)
        .ToList());

    var normalized = method.Replace("-", "", StringComparison.Ordinal);
    return methods.FirstOrDefault(m =>
      string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(m.Name, normalized, StringComparison.OrdinalIgnoreCase));
  }

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
