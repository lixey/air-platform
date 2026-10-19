namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomStockTransferFlowService? flow = null)
  : EcomDocHistoryGridService<EcomStockTransferFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.StockTransfer;
}
