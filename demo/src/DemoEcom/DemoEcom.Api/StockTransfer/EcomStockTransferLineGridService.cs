namespace DemoEcom.StockTransfer;

public sealed class EcomStockTransferLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomStockTransferFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockTransfer;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomStockTransferLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
