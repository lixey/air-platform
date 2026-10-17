namespace DemoEcom.StockIssue;

public sealed class EcomStockIssueGridService(
  EcomDbContext db, EcomStockIssueFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomData.DocType.StockIssueId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockIssue;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockIssueFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
  }
}
