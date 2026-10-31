namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestReceiptLine : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public long ReceiptId { get; set; }
  public string Name { get; set; } = string.Empty;
  public decimal Amount { get; set; }
}
