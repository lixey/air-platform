using Air.App.Form;
using Air.App.Grid;
using Air.App.Modules;
using DemoOrg.Data;
using DemoOrg.Data.Models;

namespace DemoOrg.Product;

public sealed class OrgProductGridService(OrgDbContext db) : GridService<OrgProduct, OrgProduct>(db)
{
  public override Feature Feature => Org.Product;
  protected override string FormServicePath => "org/productform";

  protected override void SetColumns(Columns<OrgProduct> columns)
  {
    columns.Add(r => r.Id).Label("ID").AsOpenForm<OrgProductFormService>();
    columns.Add(r => r.Article).Label("Article / SKU");
    columns.Add(r => r.Name).Label("Product Name");
    columns.Add(r => r.Price).Label("Selling Price").AsNumber();
    columns.Add(r => r.PurchasePrice).Label("Purchase Price").AsNumber();
    columns.Add(r => r.StockQuantity).Label("In Stock").AsNumber();
    columns.Add(r => r.IsActive).Label("Active").AsCheckbox();
  }
}

public sealed class OrgProductFormService(OrgDbContext db) : FormService<OrgProduct>(db)
{
  public override Feature Feature => Org.Product;
  protected override string Title => "Product";

  protected override void SetElements(Elements<OrgProduct> elements)
  {
    elements.Add(e => e.Article).Label("Article / SKU").Span(4);
    elements.Add(e => e.Name).Label("Product Name").Span(8);
    elements.Add(e => e.Price).Label("Selling Price").Span(4);
    elements.Add(e => e.PurchasePrice).Label("Purchase Cost").Span(4);
    elements.Add(e => e.StockQuantity).Label("Stock Quantity").Span(4);
    elements.Add(e => e.IsActive).Label("Active").AsCheckbox().Span(12);
  }
}
