namespace DemoEcom.MoneyBalance;

public sealed class EcomMoneyBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyBalance>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyBalance;

  protected override void SetColumns(Columns<EcomMoneyBalance> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
