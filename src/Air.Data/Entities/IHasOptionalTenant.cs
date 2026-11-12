namespace Platform.Data.Entities;

/// <summary>
/// Marks an entity whose rows belong either to a specific tenant (<see cref="TenantId"/> set) or globally to all tenants (<see cref="TenantId"/> is <c>null</c>).
/// </summary>
public interface IHasOptionalTenant
{
  long? TenantId { get; set; }
}
