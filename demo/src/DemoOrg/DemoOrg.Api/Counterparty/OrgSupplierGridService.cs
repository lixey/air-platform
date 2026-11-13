namespace DemoOrg.Counterparty;

public sealed class OrgSupplierGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;
  protected override bool IsReadOnly => true;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id);
    Columns.Add(r => r.Name);
  }

  protected override void SetQuery() =>
    Query.Where(counterparty => counterparty.IsSupplier && counterparty.IsActive).RecordLabel(c => c.Name);
}

