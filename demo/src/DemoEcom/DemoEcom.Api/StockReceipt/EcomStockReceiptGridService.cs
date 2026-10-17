namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptGridService(
  EcomDbContext db, EcomStockReceiptFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomData.DocType.StockReceiptId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockReceipt;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockReceiptFormService>();
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
