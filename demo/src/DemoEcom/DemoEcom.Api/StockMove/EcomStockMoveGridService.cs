namespace DemoEcom.StockMove;

public sealed class EcomStockMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockMove>(db, ecom)
{
  public override Feature Feature => Ecom.StockMove;

  protected override void SetQuery()
  {
    Query.SortByDescending(r => r.Date).ThenByDescending(r => r.Id);
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Date);
    Columns.Add(r => r.Doc.Number);
    Columns.Add(r => r.Doc.DocType.Name).Label("Type");
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(move => move.Organization.Color.Handle);
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Reserved);
    Columns.Add(r => r.Expected);
  }
}
