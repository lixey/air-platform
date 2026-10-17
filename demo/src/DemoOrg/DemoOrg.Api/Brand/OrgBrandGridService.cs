namespace DemoOrg.Brand;

public sealed class OrgBrandGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override void SetColumns(Columns<OrgBrand> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgBrandFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
