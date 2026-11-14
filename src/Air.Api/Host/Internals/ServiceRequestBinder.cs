namespace Platform.Api.Host.Internals;

internal static class ServiceRequestBinder
{
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

  public static void ApplyScopedHttpMetadata(
    HttpRequest httpRequest,
    IScopedServiceRequest req,
    JsonElement? rawBody = null)
  {
    ApplyScopedQueryParams(httpRequest, req);
    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      req.IdempotencyKey = idemVals.ToString().Trim();
      if (rawBody is { ValueKind: not JsonValueKind.Undefined })
      {
        req.RawPayload ??= rawBody.Value.GetRawText();
      }
    }
  }

  public static async Task<(JsonElement? Body, bool IsMalformed)> TryReadJsonBodyAsync(
    HttpRequest httpRequest,
    CancellationToken ct)
  {
    if (!ServiceEndpointsExtensions.HasJsonBody(httpRequest) ||
        httpRequest.HasFormContentType ||
        httpRequest.Body.CanSeek && httpRequest.Body.Length == 0)
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
