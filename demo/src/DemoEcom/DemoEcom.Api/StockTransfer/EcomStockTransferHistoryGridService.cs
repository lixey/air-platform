namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomStockTransferFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockTransfer;
}
