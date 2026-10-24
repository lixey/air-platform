namespace Platform.Data.Entities;

/// <summary>
/// A row a tenant owns and the tenants under it inherit, or a global row that belongs to no tenant.
/// </summary>
public interface IInheritedByChildTenants
{
  long? TenantId { get; set; }
}
