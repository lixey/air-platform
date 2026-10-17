using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(Name), IsUnique = true)]
public sealed class OrgPaymentMethod : AppBaseModel
{
  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  public bool IsCash { get; set; }

  public bool IsCashOnDelivery { get; set; }

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }
}
