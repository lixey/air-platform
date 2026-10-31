namespace Platform.Api.Tests.Internals;

internal sealed class ApiTestItem : IEntity, IHasTenant, IHasVersion
{
  public long Id { get; set; }
  public long TenantId { get; set; }
  public int Ver { get; set; }
  public string Name { get; set; } = string.Empty;
}
