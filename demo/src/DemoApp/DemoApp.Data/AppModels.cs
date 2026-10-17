using Air.Data.Entities;

namespace DemoApp.Data.Models;

public class AppTenant : BaseDictionaryEntity
{
  public string? TimeZone { get; set; } = "UTC";
  public long? ParentId { get; set; }
  public AppTenant? Parent { get; set; }
}

public class AppUser : BaseEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public string Login { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string Email { get; set; } = string.Empty;
  public string? PasswordHash { get; set; }
  public bool IsActive { get; set; } = true;
  public bool MustChangePassword { get; set; }
  public string? TimeZone { get; set; } = "UTC";
  public long? UserGroupId { get; set; }
  public AppUserGroup? UserGroup { get; set; }
  public AppTenant? Tenant { get; set; }
}

public class AppAuthRole : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public AppTenant? Tenant { get; set; }
}

public class AppUserGroup : BaseDictionaryEntity, IHasTenant
{
  public long TenantId { get; set; } = 1;
  public string? Color { get; set; } = "indigo";
  public AppTenant? Tenant { get; set; }
}

public class AppAuthAssignment : BaseEntity
{
  public long UserId { get; set; }
  public long AuthRoleId { get; set; }
  public bool IsActive { get; set; } = true;
}

public class AppAuthGrant : BaseEntity
{
  public long AuthRoleId { get; set; }
  public string Permission { get; set; } = string.Empty;
}

public class AppLanguage : BaseDictionaryEntity
{
}

public class AppColor : BaseDictionaryEntity
{
}
