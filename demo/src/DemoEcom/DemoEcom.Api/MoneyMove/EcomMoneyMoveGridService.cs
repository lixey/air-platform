namespace DemoEcom.MoneyMove;

public sealed class EcomMoneyMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyMove>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyMove;

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.Date).ThenByDescending(r => r.Id);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Date);
    columns.Add(r => r.Doc.Number);
    columns.Add(r => r.Doc.DocType.Name).Label("Type");
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(move => move.Organization.Color.Handle);
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Amount).AsMoney();
  }
}
