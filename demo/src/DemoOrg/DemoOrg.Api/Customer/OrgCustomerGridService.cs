namespace DemoOrg.Customer;

public sealed class OrgCustomerGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCustomer>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetColumns(Columns<OrgCustomer> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgCustomerFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Phone);
    columns.Add(r => r.Email);
    columns.Add(r => r.City);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
