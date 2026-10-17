using DemoEcom.Documents;

namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomStockTransferFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetColumns(Columns<EcomDocLineRow> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockTransferLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
