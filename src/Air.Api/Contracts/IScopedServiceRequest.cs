namespace Platform.Api.Contracts;

public interface IScopedServiceRequest
{
  long? Id { get; set; }
  long? ParentId { get; set; }
  string? ParentService { get; set; }
  string? IdempotencyKey { get; set; }
  string? RawPayload { get; set; }
  bool HandlesOwnJsonDeserialization => false;
  void BindJsonBody(JsonElement body) { }
}
