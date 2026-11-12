namespace Platform.Agent.Tests.Internals;

internal sealed class TestLeafOneItem : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public string Code { get; set; } = string.Empty;
  public long? CategoryId { get; set; }
}
