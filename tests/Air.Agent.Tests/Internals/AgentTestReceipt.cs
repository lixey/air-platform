namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestReceipt : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public string Title { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public long? CategoryId { get; set; }
}
