using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(Name), IsUnique = true)]
public sealed class OrgProductCategory : AppBaseModel
{
  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  public long ColorId { get; set; }
  public AppColor Color { get; set; } = null!;

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }
}
