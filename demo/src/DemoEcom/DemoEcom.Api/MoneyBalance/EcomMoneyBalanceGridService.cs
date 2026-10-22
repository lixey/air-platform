namespace DemoEcom.MoneyBalance;

public sealed class EcomMoneyBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyBalance, EcomMoneyBalanceRow>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyBalance;

  protected override void SetQuery(Query query)
  {
    query.Select(balance => new EcomMoneyBalanceRow
    {
      Id = balance.Id,
      Organization = new Reference { Id = balance.Organization.Id, Name = balance.Organization.Name },
      MoneyAccount = new Reference { Id = balance.MoneyAccount.Id, Name = balance.MoneyAccount.Name },
      IsCash = balance.MoneyAccount.IsCash,
      Amount = balance.Amount
    });

    query.SortBy(r => r.Organization).ThenBy(r => r.MoneyAccount);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(balance => balance.Organization.Color.Handle);
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.IsCash).AsCheckbox();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
