namespace DemoEcom.MoneyMove;

public sealed class EcomMoneyMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyMove>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyMove;

  protected override void SetQuery()
  {
    Query.SortByDescending(r => r.Date).ThenByDescending(r => r.Id);
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Date);
    Columns.Add(r => r.Doc.Number);
    Columns.Add(r => r.Doc.DocType.Name).Label("Type");
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(move => move.Organization.Color.Handle);
    Columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    Columns.Add(r => r.Amount).AsMoney();
  }
}
