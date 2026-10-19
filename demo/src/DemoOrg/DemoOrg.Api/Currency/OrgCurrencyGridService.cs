namespace DemoOrg.Currency;

public sealed class OrgCurrencyGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCurrency>(db, org)
{
  public override Feature Feature => Org.Currency;

  protected override void SetColumns(Columns<OrgCurrency> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgCurrencyFormService>();
    columns.Add(r => r.Handle);
    columns.Add(r => r.Name);
    columns.Add(r => r.Symbol);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
