namespace Platform.App.Tests.Internals;

internal sealed class FixedTenant(long tenantId, long userId, IReadOnlySet<string> rights) : ITenantContext
{
  public long? TenantId => tenantId;
  public long? UserId => userId;
  public IReadOnlySet<string>? Rights => rights;
  public IDisposable BeginScope(long? t, long? u = null, IReadOnlySet<string>? r = null) => new MemoryStream();
}
