namespace Platform.Data.Tests.Internals;

internal sealed class TestOrderNote : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public long? OrderId { get; set; }
  public TestOrder? Order { get; set; }
  public string Note { get; set; } = string.Empty;
}
