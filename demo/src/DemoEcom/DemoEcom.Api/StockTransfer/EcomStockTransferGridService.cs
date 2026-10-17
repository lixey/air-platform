using Air.App.Grid;
using DemoEcom.Data;
using DemoEcom.Documents;

namespace DemoEcom.StockTransfer;

/// <summary>
/// The active tenant's stock transfers, the newest first.
/// </summary>
public sealed class EcomStockTransferGridService(
  EcomDbContext db, EcomStockTransferFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockTransferId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockTransferFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Warehouse).Label("From warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.WarehouseTo).Label("To warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
