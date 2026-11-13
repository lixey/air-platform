namespace DemoOrg.Customer;

public sealed class OrgCustomerContactGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgCustomerContact>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetParent() => Parent.Bind<OrgCustomerFormService>(c => c.CustomerId, optional: true);

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgCustomerContactFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Position);
    Columns.Add(r => r.Phone).AsPhone();
    Columns.Add(r => r.Email).AsEmail();
    Columns.Add(r => r.IsPrimary).AsCheckbox();

  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}
