namespace DemoEcom.MoneyMove;

public sealed class EcomMoneyMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyMove>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyMove;

  protected override void SetColumns(Columns<EcomMoneyMove> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Date);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetQuery(Query query) => query.SortByDescending(r => r.Date);
}
