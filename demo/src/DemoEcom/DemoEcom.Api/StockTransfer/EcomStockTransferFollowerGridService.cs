namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomStockTransferFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockTransfer;
}
