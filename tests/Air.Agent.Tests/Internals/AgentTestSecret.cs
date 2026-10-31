namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestSecret : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public string Code { get; set; } = string.Empty;
}
