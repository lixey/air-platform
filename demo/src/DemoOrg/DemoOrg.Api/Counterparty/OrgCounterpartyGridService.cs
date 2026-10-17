namespace DemoOrg.Counterparty;

public sealed class OrgCounterpartyGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;

  protected override void SetColumns(Columns<OrgCounterparty> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgCounterpartyFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.TaxId);
    columns.Add(r => r.IsSupplier).AsCheckbox();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
