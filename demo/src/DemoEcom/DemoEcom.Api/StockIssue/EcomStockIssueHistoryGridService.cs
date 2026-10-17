namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomStockIssueFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockIssue;
}
