namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestAuditNote : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public long InspectionId { get; set; }
  public string Summary { get; set; } = string.Empty;
}
