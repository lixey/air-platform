namespace DemoOrg.Customer;

public sealed class OrgCustomerContactGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCustomerContact>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetParent(Parent parent) => parent.Bind<OrgCustomerFormService>(c => c.CustomerId, optional: true);

  protected override void SetColumns(Columns<OrgCustomerContact> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgCustomerContactFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Position);
    columns.Add(r => r.Phone).AsPhone();
    columns.Add(r => r.Email).AsEmail();
    columns.Add(r => r.IsPrimary).AsCheckbox();

  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
