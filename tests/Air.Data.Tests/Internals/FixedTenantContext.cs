namespace Platform.Data.Tests.Internals;

internal sealed class FixedTenantContext(long tenantId, long userId) : ITenantContext
{
  public long? TenantId => tenantId;
  public long? UserId => userId;
  public IReadOnlySet<string>? Rights => null;
  public IDisposable BeginScope(long? t, long? u = null, IReadOnlySet<string>? r = null) => new MemoryStream();
}
