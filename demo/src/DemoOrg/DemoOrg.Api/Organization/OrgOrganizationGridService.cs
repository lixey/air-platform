using DemoApp.Color;

namespace DemoOrg.Organization;

public sealed class OrgOrganizationGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgOrganization>(db, org)
{
  public override Feature Feature => Org.Organization;

  protected override void SetColumns(Columns<OrgOrganization> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgOrganizationFormService>();
    columns.Add(r => r.Name).Color(o => o.Color.Handle);
    columns.Add(r => r.Code);
    columns.Add(r => r.TaxId);
    columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(o => o.Color.Handle);
    columns.Add(r => r.Phone).AsPhone();
    columns.Add(r => r.Email).AsEmail();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
