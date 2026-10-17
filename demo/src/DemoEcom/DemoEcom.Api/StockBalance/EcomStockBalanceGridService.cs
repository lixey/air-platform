namespace DemoEcom.StockBalance;

public sealed class EcomStockBalanceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomStockBalance>(db, ecom)
{
  public override Feature Feature => Ecom.StockBalance;

  protected override void SetColumns(Columns<EcomStockBalance> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Reserved).Label("Reserved").Sum();
    columns.Add(r => r.Expected).Label("Expected").Sum();
  }
}
