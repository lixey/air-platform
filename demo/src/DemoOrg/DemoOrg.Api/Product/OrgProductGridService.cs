namespace DemoOrg.Product;

/// <summary>
/// The active tenant's products, by name: each with its cover, the first of its photos, its article, its brand and
/// supplier, its category painted in the category's color, its unit and its two prices in hryvnias. Its search reads
/// the id, the article, the name and the names of the brand, the supplier, the category and the unit.
/// </summary>
public sealed class OrgProductGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgProduct>(db, org)
{
  public override Feature Feature => Org.Product;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgProductFormService>();
    Columns.Add(r => r.Id).AsCover<OrgProductFileGridService>().Width(72);
    Columns.Add(r => r.Article);
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Brand).AsReference<OrgBrandGridService>();
    Columns.Add(r => r.Supplier).AsReference<OrgSupplierGridService>();
    Columns.Add(r => r.Category).AsReference<OrgProductCategoryGridService>().Color(p => p.Category!.Color.Handle);
    Columns.Add(r => r.Unit).AsReference<OrgUnitGridService>();
    Columns.Add(r => r.Price).AsMoney(OrgProductFormService.Currency);
    Columns.Add(r => r.PurchasePrice).AsMoney(OrgProductFormService.Currency);
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery()
  {
    Query.SortBy(r => r.Name);
    Query.RecordLabel(e => e.Article + " " + e.Name);
    Query.ReferenceLabel(r => r.Article, r => r.Name);
    Query.SearchBy(r => r.Id, r => r.Article, r => r.Name, r => r.Brand, r => r.Supplier, r => r.Category, r => r.Unit);
  }

  protected override void SetHooks() =>
    Hooks.DeleteFiles<OrgProductFile>(f => f.ProductId);
}
