namespace Platform.App.Tests.Internals;

internal sealed class FixedTenant(long tenantId, long userId, IReadOnlySet<string> rights) : ITenantContext
{
  public long? TenantId => AsyncLocalTenantContext.Instance.HasExplicitSystemScope ? null : tenantId;
  public bool IsSystemScope => AsyncLocalTenantContext.Instance.HasExplicitSystemScope;
  public long? UserId => AsyncLocalTenantContext.Instance.HasExplicitSystemScope ? null : userId;
  public IReadOnlySet<string>? Rights => rights;
  public IDisposable BeginScope(long? t, long? u = null, IReadOnlySet<string>? r = null) => new MemoryStream();
}
