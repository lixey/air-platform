namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSupplierInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSupplierInvoiceLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.QtyDone).Label("Returned").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
