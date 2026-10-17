using System.ComponentModel.DataAnnotations;
using DemoApp.Data.Models;

namespace DemoOrg.Data.Models;

[Index(nameof(TenantId), nameof(OrganizationId), nameof(Name), IsUnique = true)]
public sealed class OrgMoneyAccount : AppBaseModel
{
  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  [Required, MaxLength(128)]
  public string Name { get; set; } = string.Empty;

  public bool IsCash { get; set; }

  public long CurrencyId { get; set; }
  public OrgCurrency Currency { get; set; } = null!;

  [MaxLength(34)]
  public string? Iban { get; set; }

  [MaxLength(128)]
  public string? BankName { get; set; }

  public bool IsActive { get; set; } = true;

  public int SortOrder { get; set; }
}
