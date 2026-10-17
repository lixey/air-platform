namespace DemoApp.Data.Models;

/// <summary>
/// A role of a tenant, inherited by the tenants under it, or a global role of none.
/// </summary>
[Index(nameof(TenantId), nameof(Name), IsUnique = true)]
public sealed class AppAuthRole : BaseAppAuthRole
{
  public AppTenant? Tenant { get; set; }
}
