namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomStockIssueFlowService? flow = null)
  : EcomDocHistoryGridService<EcomStockIssueFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.StockIssue;
}
