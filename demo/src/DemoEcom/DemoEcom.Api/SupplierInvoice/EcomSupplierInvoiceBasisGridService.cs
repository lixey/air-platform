namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomSupplierInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierInvoice;
}
