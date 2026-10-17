namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomSalesInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesInvoice;
}
