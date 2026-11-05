namespace DemoEcom.MoneyBalance;

public sealed class EcomMoneyBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyBalance>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyBalance;

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.Organization).ThenBy(r => r.MoneyAccount);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(balance => balance.Organization.Color.Handle);
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.MoneyAccount.IsCash).AsCheckbox();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
