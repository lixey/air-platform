namespace Air.Data.Tenancy;

/// <summary>
/// Provides access to the current tenant identifier for multi-tenant data scoping.
/// </summary>
public interface ITenantContext
{
  long? TenantId { get; }
}
