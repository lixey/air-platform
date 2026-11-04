namespace Platform.Api.Contracts;

public interface IScopedServiceRequest
{
  long? Id { get; set; }
  long? ParentId { get; set; }
  string? ParentService { get; set; }
}
