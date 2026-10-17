namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferGridService(
  EcomDbContext db, EcomStockTransferFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom, flow)
{
  protected override long Type => EcomData.DocType.StockTransferId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockTransferFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.Warehouse).Label("From warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.WarehouseTo).Label("To warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity");
    columns.Add(r => r.Amount).AsMoney();
  }
}
