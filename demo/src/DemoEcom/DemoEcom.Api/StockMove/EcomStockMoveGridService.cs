namespace DemoEcom.StockMove;

public sealed class EcomStockMoveGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockMove>(db, ecom)
{
  public override Feature Feature => Ecom.StockMove;

  protected override void SetColumns(Columns<EcomStockMove> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Date);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Reserved).Label("Reserved").Sum();
    columns.Add(r => r.Expected).Label("Expected").Sum();
  }

  protected override void SetQuery(Query query) => query.SortByDescending(r => r.Date);
}
