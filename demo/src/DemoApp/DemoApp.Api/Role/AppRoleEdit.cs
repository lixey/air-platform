using System.ComponentModel.DataAnnotations;

namespace DemoApp.Role;

public sealed class AppRoleEdit
{
  [Required]
  public string Name { get; set; } = string.Empty;

  public string? Description { get; set; }

  public int SortOrder { get; set; } = 10;
  public bool IsActive { get; set; } = true;
  public long? TenantId { get; set; }
  public string[]? Permissions { get; set; } = [];
}
