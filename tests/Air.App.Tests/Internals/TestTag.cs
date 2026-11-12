namespace Platform.App.Tests.Internals;

internal sealed class TestTag : IEntity, IHasTenant
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public string Name { get; set; } = string.Empty;
  public bool IsActive { get; set; } = true;
  public decimal DefaultPrice { get; set; }
  public long? CategoryId { get; set; }
  public string? Entity { get; set; }
  public string? Value { get; set; }
}
