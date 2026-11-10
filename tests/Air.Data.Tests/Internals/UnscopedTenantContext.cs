namespace Platform.Data.Tests.Internals;

internal sealed class UnscopedTenantContext(long? tenantId = null, bool isSystemScope = false) : ITenantContext
{
  public long? TenantId => tenantId;
  public bool IsSystemScope => isSystemScope;
  public long? UserId => 42;
  public string? CorrelationId => "test-corr";
  public IReadOnlySet<string>? Rights => null;
}
