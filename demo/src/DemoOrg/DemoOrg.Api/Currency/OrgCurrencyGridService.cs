namespace DemoOrg.Currency;

public sealed class OrgCurrencyGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCurrency>(db, org)
{
  public override Feature Feature => Org.Currency;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgCurrencyFormService>();
    Columns.Add(r => r.Handle);
    Columns.Add(r => r.Code);
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Symbol);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}
