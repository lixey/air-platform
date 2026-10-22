namespace Air.Data.Entities;

[Comment("Role grant: a right's key or a pattern")]
public abstract class BaseAppAuthGrant : BaseEntity
{
  [Comment("Role identifier")]
  public long AuthRoleId { get; set; }

  [Required, MaxLength(200), Comment("Right's key or pattern: *, Module.*, Module.Feature.*, Module.Feature.Action")]
  public string Permission { get; set; } = string.Empty;
}
