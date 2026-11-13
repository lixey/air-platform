namespace Platform.App.Files.Contracts;

[UiContract]
public sealed class GridUploadRequest : IMultipartServiceRequest
{
  public string? ParentService { get; set; }
  public long? ParentId { get; set; }
  public long? AfterId { get; set; }
  public string? Metadata { get; set; }

  [JsonIgnore]
  public string? IdempotencyKey { get; set; }

  [JsonIgnore]
  public string? RawPayload { get; set; }

  public void BindMultipart(HttpRequest httpRequest, IFormCollection form)
  {
    if (form.TryGetValue("parentService", out var psVal))
    {
      ParentService = psVal;
    }

    if (form.TryGetValue("parentId", out var pidVal) && long.TryParse(pidVal, out var parsedPid))
    {
      ParentId = parsedPid;
    }

    if (form.TryGetValue("afterId", out var aidVal) && long.TryParse(aidVal, out var parsedAid))
    {
      AfterId = parsedAid;
    }

    if (form.TryGetValue("metadata", out var metaVal))
    {
      Metadata = metaVal;
    }

    if (ParentId is null &&
        httpRequest.Query.TryGetValue("parentId", out var qPid) &&
        long.TryParse(qPid, out var parsedQPid))
    {
      ParentId = parsedQPid;
    }

    if (string.IsNullOrEmpty(ParentService) &&
        httpRequest.Query.TryGetValue("parentService", out var qPs))
    {
      ParentService = qPs;
    }

    if (httpRequest.Headers.TryGetValue("Idempotency-Key", out var idemVals) && !string.IsNullOrWhiteSpace(idemVals))
    {
      IdempotencyKey = idemVals.ToString().Trim();
    }

    var primaryFile = form.Files["file"] ?? form.Files.FirstOrDefault();
    RawPayload = $"{ParentService}|{ParentId}|{AfterId}|{Metadata}|{primaryFile?.FileName}|{primaryFile?.Length}";
  }
}
