using DemoApp.Color;

namespace DemoOrg.ProductCategory;

public sealed class OrgProductCategoryGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgProductCategory>(db, org)
{
  public override Feature Feature => Org.ProductCategory;

  protected override void SetColumns(Columns<OrgProductCategory> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgProductCategoryFormService>();
    columns.Add(r => r.Name).Color(c => c.Color.Handle);
    columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(c => c.Color.Handle);
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
