namespace DemoOrg.Counterparty;

public sealed class OrgSupplierGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;
  protected override bool IsReadOnly => true;

  protected override void SetColumns(Columns<OrgCounterparty> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Name);
  }

  protected override void SetQuery(Query query) =>
    query.Where(counterparty => counterparty.IsSupplier && counterparty.IsActive).RecordLabel(c => c.Name);
}

