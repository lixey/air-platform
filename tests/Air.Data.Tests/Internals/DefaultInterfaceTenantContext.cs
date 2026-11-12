namespace Platform.Data.Tests.Internals;

internal sealed class DefaultInterfaceTenantContext(long? tenantId) : ITenantContext
{
  public long? TenantId => tenantId;
}
