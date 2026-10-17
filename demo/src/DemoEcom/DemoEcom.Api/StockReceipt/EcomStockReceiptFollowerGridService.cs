namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomStockReceiptFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockReceipt;
}
