namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptGridService(
  EcomDbContext db, EcomStockReceiptFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomDocTypeData.StockReceiptId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockReceipt;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomStockReceiptFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
  }
}
