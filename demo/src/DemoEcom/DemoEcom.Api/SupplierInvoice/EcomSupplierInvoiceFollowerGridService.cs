namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomSupplierInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierInvoice;
}
