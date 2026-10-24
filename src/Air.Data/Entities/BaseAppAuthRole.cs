namespace Platform.Data.Entities;

[Comment("Base user role model")]
public abstract class BaseAppAuthRole : BaseEntity, IInheritedByChildTenants
{
  [Comment("Owning tenant, inherited by the tenants under it; null for a global role")]
  public long? TenantId { get; set; }

  [MaxLength(128), Comment("Role display name")]
  public string Name { get; set; } = string.Empty;

  [MaxLength(512), Comment("Role description")]
  public string? Description { get; set; }

  [Comment("Sort order in UI")]
  public int SortOrder { get; set; } = 10;

  [Comment("Active status flag")]
  public bool IsActive { get; set; } = true;
}
