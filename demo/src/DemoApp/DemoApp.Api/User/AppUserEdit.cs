using System.ComponentModel.DataAnnotations;

namespace DemoApp.User;

public sealed class AppUserEdit
{
  [Required]
  public string Login { get; set; } = string.Empty;

  public string? Name { get; set; }

  [EmailAddress]
  public string? Email { get; set; }

  public bool IsActive { get; set; } = true;
  public bool MustChangePassword { get; set; } = true;
  public long? LanguageId { get; set; }
  public string? TimeZone { get; set; }
  public long? UserGroupId { get; set; }
  public string? Password { get; set; }
  public long[]? RoleIds { get; set; } = [];
}
