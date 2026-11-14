using System.Text.Json;

namespace Platform.Api.Tests.Internals;

internal sealed class CustomScopedPayload : IScopedServiceRequest
{
  public long? Id { get; set; }
  public long? ParentId { get; set; }
  public string? ParentService { get; set; }
  public string? IdempotencyKey { get; set; }
  public string? RawPayload { get; set; }

  bool IScopedServiceRequest.HandlesOwnJsonDeserialization => true;

  void IScopedServiceRequest.BindJsonBody(JsonElement body)
  {
    if (body.ValueKind != JsonValueKind.Object)
    {
      return;
    }

    if (Id is null or <= 0 &&
        body.TryGetProperty("id", out var idProp) &&
        idProp.ValueKind == JsonValueKind.Number &&
        idProp.TryGetInt64(out var bodyId))
    {
      Id = bodyId;
    }

    if (ParentId is null &&
        body.TryGetProperty("parentId", out var pidProp) &&
        pidProp.ValueKind == JsonValueKind.Number &&
        pidProp.TryGetInt64(out var bodyPid))
    {
      ParentId = bodyPid;
    }
  }
}
