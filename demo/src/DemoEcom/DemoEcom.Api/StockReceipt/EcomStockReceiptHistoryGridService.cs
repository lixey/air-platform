namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomStockReceiptFlowService? flow = null)
  : EcomDocHistoryGridService<EcomStockReceiptFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.StockReceipt;
}
