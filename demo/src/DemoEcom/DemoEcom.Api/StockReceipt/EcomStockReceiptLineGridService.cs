namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomStockReceiptFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockReceipt;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomStockReceiptLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
