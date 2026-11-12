namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestInspection : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public string Code { get; set; } = string.Empty;
  public long Status { get; set; } = 1;
  public string? Notes { get; set; }
}
