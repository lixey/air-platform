namespace Platform.App.Tests.Internals;

internal sealed class TestTag : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public string Name { get; set; } = string.Empty;
  public bool IsActive { get; set; } = true;
}
