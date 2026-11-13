namespace DemoEcom.MoneyBalance;

public sealed class EcomMoneyBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyBalance>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyBalance;

  protected override void SetQuery()
  {
    Query.SortBy(r => r.Organization).ThenBy(r => r.MoneyAccount);
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(balance => balance.Organization.Color.Handle);
    Columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    Columns.Add(r => r.MoneyAccount.IsCash).AsCheckbox();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
