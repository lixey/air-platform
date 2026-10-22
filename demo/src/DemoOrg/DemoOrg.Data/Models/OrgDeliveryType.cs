namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(Name), IsUnique = true)]
public sealed class OrgDeliveryType : AppBaseModel
{
  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  public bool IsPickup { get; set; }

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }
}
