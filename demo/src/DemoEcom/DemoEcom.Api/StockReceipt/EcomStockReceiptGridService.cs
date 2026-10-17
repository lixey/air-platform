using Air.App.Grid;
using DemoEcom.Data;
using DemoEcom.Documents;

namespace DemoEcom.StockReceipt;

/// <summary>
/// The active tenant's stock receipts, the newest first.
/// </summary>
public sealed class EcomStockReceiptGridService(
  EcomDbContext db, EcomStockReceiptFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockReceiptId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.StockReceipt;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockReceiptFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Manager).AsReference<AppUserGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
