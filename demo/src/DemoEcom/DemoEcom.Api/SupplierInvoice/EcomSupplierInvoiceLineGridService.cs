namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSupplierInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierInvoice;

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSupplierInvoiceLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
