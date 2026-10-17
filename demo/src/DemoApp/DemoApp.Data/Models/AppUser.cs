namespace DemoApp.Data.Models;

[Index(nameof(Login), IsUnique = true), Index(nameof(Email), IsUnique = true), Index(nameof(TenantId), nameof(IsActive))]
public sealed class AppUser : BaseAppUser, IHasTenant
{
  public long TenantId { get; set; }
  public AppTenant? Tenant { get; set; }

  public long? UserGroupId { get; set; }
  public AppUserGroup? UserGroup { get; set; }
}
