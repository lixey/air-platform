namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSalesInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetColumns(Columns<EcomDocLineRow> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesInvoiceLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.QtyDone).Label("Returned").Sum();
    columns.Add(r => r.Price).AsMoney();
    columns.Add(r => r.DiscountPercent).Label("Discount").AsPercent();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
