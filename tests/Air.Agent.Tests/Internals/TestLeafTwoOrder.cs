namespace Platform.Agent.Tests.Internals;

internal sealed class TestLeafTwoOrder : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public string ReferenceNumber { get; set; } = string.Empty;
  public int Quantity { get; set; }
  public long? CategoryId { get; set; }
}
