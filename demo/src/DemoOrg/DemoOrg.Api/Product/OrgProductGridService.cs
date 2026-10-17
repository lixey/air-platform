namespace DemoOrg.Product;

public sealed class OrgProductGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgProduct>(db, org)
{
  public override Feature Feature => Org.Product;

  protected override void SetColumns(Columns<OrgProduct> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgProductFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Article);
    columns.Add(r => r.Brand).AsReference<OrgBrandGridService>();
    columns.Add(r => r.Category).AsReference<OrgProductCategoryGridService>();
    columns.Add(r => r.Unit).AsReference<OrgUnitGridService>();
    columns.Add(r => r.Price).AsMoney();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
