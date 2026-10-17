namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomStockReceiptFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockReceipt;
}
