namespace DemoEcom.SupplierInvoice;

public sealed class EcomSupplierInvoiceHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomSupplierInvoiceFlowService? flow = null)
  : EcomDocHistoryGridService<EcomSupplierInvoiceFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.SupplierInvoice;
}
