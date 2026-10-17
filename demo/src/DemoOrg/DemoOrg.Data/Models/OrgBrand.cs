using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(Name), IsUnique = true)]
public sealed class OrgBrand : AppBaseModel
{
  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(64)]
  public string? Country { get; set; }

  [MaxLength(256)]
  public string? Website { get; set; }

  public bool IsActive { get; set; } = true;
}
