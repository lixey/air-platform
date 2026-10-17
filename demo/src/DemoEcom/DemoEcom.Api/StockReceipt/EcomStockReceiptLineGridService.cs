using DemoEcom.Documents;

namespace DemoEcom.StockReceipt;

public sealed class EcomStockReceiptLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomStockReceiptFormService>(db, ecom)
{
  public override Feature Feature => Ecom.StockReceipt;

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomStockReceiptLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Price).AsMoney();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
