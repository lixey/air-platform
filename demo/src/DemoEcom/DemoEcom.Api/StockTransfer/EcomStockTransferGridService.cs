namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.StockTransferId;

  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockTransferFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Warehouse).Label("Source Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.WarehouseTo).Label("Destination Warehouse").AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
