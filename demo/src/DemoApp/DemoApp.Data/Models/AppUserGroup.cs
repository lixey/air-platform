namespace DemoApp.Data.Models;

/// <summary>
/// A group of a tenant's users, marked by a color.
/// </summary>
[Index(nameof(TenantId), nameof(Name), IsUnique = true)]
public sealed class AppUserGroup : AppBaseModel
{
  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(512)]
  public string? Description { get; set; }

  public long ColorId { get; set; }
  public AppColor Color { get; set; } = null!;

  public bool IsActive { get; set; } = true;
}
