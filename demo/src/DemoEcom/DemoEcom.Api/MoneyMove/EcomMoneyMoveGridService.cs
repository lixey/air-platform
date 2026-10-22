namespace DemoEcom.MoneyMove;

public sealed class EcomMoneyMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomMoneyMove, EcomMoneyMoveRow>(db, ecom)
{
  public override Feature Feature => Ecom.MoneyMove;

  protected override void SetQuery(Query query)
  {
    query.Select(move => new EcomMoneyMoveRow
    {
      Id = move.Id,
      Date = move.Date,
      Number = move.Doc.Number,
      Type = move.Doc.DocType.Name,
      Organization = new Reference { Id = move.Organization.Id, Name = move.Organization.Name },
      MoneyAccount = new Reference { Id = move.MoneyAccount.Id, Name = move.MoneyAccount.Name },
      Amount = move.Amount
    });

    query.SortByDescending(r => r.Date).ThenByDescending(r => r.Id);
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Date);
    columns.Add(r => r.Number);
    columns.Add(r => r.Type);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(move => move.Organization.Color.Handle);
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Amount).AsMoney();
  }
}
