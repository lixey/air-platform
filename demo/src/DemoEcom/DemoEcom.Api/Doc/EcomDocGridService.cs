namespace DemoEcom.Doc;

public sealed class EcomDocGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDoc>(db, ecom)
{
  public override Feature Feature => Ecom.Doc;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.Date);
    query.RecordLabel(e => e.Number);
  }
}
