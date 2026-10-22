namespace DemoEcom.Data.Models;

[Index(nameof(TenantId), nameof(OrganizationId), nameof(MoneyAccountId), IsUnique = true)]
public sealed class EcomMoneyBalance : AppBaseModel
{
  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  public long MoneyAccountId { get; set; }
  public OrgMoneyAccount MoneyAccount { get; set; } = null!;

  [Precision(18, 2)]
  public decimal Amount { get; set; }
}
