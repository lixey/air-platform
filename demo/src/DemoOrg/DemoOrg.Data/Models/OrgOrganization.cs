using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(Name), IsUnique = true), Index(nameof(TenantId), nameof(Code), IsUnique = true)]
public sealed class OrgOrganization : AppBaseModel
{
  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  [MaxLength(256)]
  public string? FullName { get; set; }

  [Required, MaxLength(8)]
  public string Code { get; set; } = string.Empty;

  [MaxLength(16)]
  public string? TaxId { get; set; }

  [MaxLength(16)]
  public string? VatNumber { get; set; }

  public bool IsVatPayer { get; set; }

  [MaxLength(512)]
  public string? Address { get; set; }

  [MaxLength(32)]
  public string? Phone { get; set; }

  [MaxLength(256)]
  public string? Email { get; set; }

  public long ColorId { get; set; }
  public AppColor Color { get; set; } = null!;

  public long CurrencyId { get; set; } = OrgData.Currency.UahId;
  public OrgCurrency Currency { get; set; } = null!;

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }

  [MaxLength(2000)]
  public string? Note { get; set; }
}
