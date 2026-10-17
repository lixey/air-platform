using Microsoft.EntityFrameworkCore;

namespace Air.Data.Entities;

[Comment("User-to-role assignment")]
public abstract class BaseAppAuthAssignment : BaseEntity
{
  [Comment("Assigned user identifier")]
  public long UserId { get; set; }

  [Comment("Assigned role identifier")]
  public long AuthRoleId { get; set; }

  [Comment("Active status flag")]
  public bool IsActive { get; set; } = true;
}
