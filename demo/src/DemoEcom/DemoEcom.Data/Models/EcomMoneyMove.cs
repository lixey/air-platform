using DemoApp.Data.Models;
using DemoOrg.Data.Models;

namespace DemoEcom.Data.Models;

[Index(nameof(TenantId), nameof(MoneyAccountId), nameof(Date)), Index(nameof(TenantId), nameof(DocId)), Index(nameof(TenantId), nameof(Date))]
public sealed class EcomMoneyMove : AppBaseModel
{
  public long DocId { get; set; }
  public EcomDoc Doc { get; set; } = null!;

  public DateOnly Date { get; set; }

  public long OrganizationId { get; set; }
  public OrgOrganization Organization { get; set; } = null!;

  public long MoneyAccountId { get; set; }
  public OrgMoneyAccount MoneyAccount { get; set; } = null!;

  [Precision(18, 2)]
  public decimal Amount { get; set; }
}
