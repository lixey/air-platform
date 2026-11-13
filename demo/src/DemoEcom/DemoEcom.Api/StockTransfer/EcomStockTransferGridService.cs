namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferGridService(
  EcomDbContext db, EcomStockTransferFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomDocTypeData.StockTransferId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomStockTransferFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.Warehouse).Label("From warehouse").AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.WarehouseTo).Label("To warehouse").AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    Columns.Add(r => r.Qty).Label("Quantity");
    Columns.Add(r => r.Amount).AsMoney();
  }
}
