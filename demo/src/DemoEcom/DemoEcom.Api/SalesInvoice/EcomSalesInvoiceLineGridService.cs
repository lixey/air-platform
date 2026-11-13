namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSalesInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSalesInvoiceLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.QtyDone).Label("Returned").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.DiscountPercent).Label("Discount").AsPercent();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
