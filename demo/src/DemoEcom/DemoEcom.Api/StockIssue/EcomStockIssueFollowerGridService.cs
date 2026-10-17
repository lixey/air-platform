namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomStockIssueFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockIssue;
}
