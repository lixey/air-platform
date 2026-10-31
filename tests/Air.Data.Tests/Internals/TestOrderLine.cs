namespace Platform.Data.Tests.Internals;

internal sealed class TestOrderLine : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public long OrderId { get; set; }
  public TestOrder? Order { get; set; }
  public string Title { get; set; } = string.Empty;
}
