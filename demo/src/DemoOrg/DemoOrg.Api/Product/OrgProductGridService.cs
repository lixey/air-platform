using DemoOrg.Brand;
using DemoOrg.Counterparty;
using DemoOrg.ProductCategory;
using DemoOrg.Unit;

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

  protected override void SetColumns(Columns<OrgProduct> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgProductFormService>();
    // columns.Add(r => r.Id).AsCover<OrgProductFileGridService>().Width(72);
    columns.Add(r => r.Article);
    columns.Add(r => r.Name);
    columns.Add(r => r.Brand).AsReference<OrgBrandGridService>();
    columns.Add(r => r.Supplier).AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Category).AsReference<OrgProductCategoryGridService>().Color(p => p.Category!.Color.Handle);
    columns.Add(r => r.Unit).AsReference<OrgUnitGridService>();
    columns.Add(r => r.Price).AsMoney(OrgProductFormService.Currency);
    columns.Add(r => r.PurchasePrice).AsMoney(OrgProductFormService.Currency);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query)
  {
    query.SortBy(r => r.Name);
    query.RecordLabel(e => e.Article + " " + e.Name);
    query.ReferenceLabel(r => r.Article, r => r.Name);
    query.SearchBy(r => r.Id, r => r.Article, r => r.Name, r => r.Brand, r => r.Supplier, r => r.Category, r => r.Unit);
  }
}
