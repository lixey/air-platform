namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestOrder : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public string Number { get; set; } = string.Empty;
  public long OrderStatusId { get; set; }
  public AgentTestOrderStatus? OrderStatus { get; set; }
  public decimal DefaultDiscountPercent { get; set; }
  public decimal Amount { get; set; }
  public string? Note { get; set; }
}
