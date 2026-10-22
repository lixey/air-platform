namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(CustomerId))]
public sealed class OrgCustomerContact : AppBaseModel
{
  public long CustomerId { get; set; }
  public OrgCustomer Customer { get; set; } = null!;

  [Required, MaxLength(256)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(128)]
  public string? Position { get; set; }

  [MaxLength(32)]
  public string? Phone { get; set; }

  [MaxLength(256)]
  public string? Email { get; set; }

  public bool IsPrimary { get; set; }

  [MaxLength(512)]
  public string? Note { get; set; }
}
