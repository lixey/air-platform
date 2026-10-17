namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomSupplierInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierInvoice;
}
