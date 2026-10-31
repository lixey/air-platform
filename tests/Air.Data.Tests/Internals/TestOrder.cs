namespace Platform.Data.Tests.Internals;

internal sealed class TestOrder : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public string Name { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public bool IsArchived { get; set; }
}
