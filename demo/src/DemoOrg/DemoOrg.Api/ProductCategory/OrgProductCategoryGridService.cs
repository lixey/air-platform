namespace DemoOrg.ProductCategory;

public sealed class OrgProductCategoryGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgProductCategory>(db, org)
{
  public override Feature Feature => Org.ProductCategory;

  protected override void SetColumns(Columns<OrgProductCategory> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgProductCategoryFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
