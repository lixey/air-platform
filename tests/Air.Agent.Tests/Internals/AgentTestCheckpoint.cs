namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestCheckpoint : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public long InspectionId { get; set; }
  public string Code { get; set; } = string.Empty;
  public DateOnly DueDate { get; set; }
}
