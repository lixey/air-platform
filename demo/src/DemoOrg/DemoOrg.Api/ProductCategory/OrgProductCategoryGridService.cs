namespace DemoOrg.ProductCategory;

public sealed class OrgProductCategoryGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgProductCategory>(db, org)
{
  public override Feature Feature => Org.ProductCategory;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgProductCategoryFormService>();
    Columns.Add(r => r.Name).Color(c => c.Color.Handle);
    Columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(c => c.Color.Handle);
    Columns.Add(r => r.IsActive).AsCheckbox();
    Columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks() =>
    Hooks.PreventDeleteWhenUsedBy<OrgProduct>(p => p.CategoryId, "The product category is used by products and cannot be deleted.");
}

