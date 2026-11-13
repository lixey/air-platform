namespace Platform.Agent.Tests.Internals;

internal sealed class AgentTestOrderLine : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public long OrderId { get; set; }
  public int SortOrder { get; set; }
  public string ItemName { get; set; } = string.Empty;
  public decimal Qty { get; set; } = 1m;
  public decimal Price { get; set; }
  public decimal DiscountPercent { get; set; }
  public decimal Amount { get; set; }
  public string? Note { get; set; }
}
