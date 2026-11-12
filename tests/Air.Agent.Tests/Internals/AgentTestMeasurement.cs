namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestMeasurement : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public long InspectionId { get; set; }
  public decimal Reading { get; set; }
  public decimal Weight { get; set; }
  public int SortOrder { get; set; }
}
