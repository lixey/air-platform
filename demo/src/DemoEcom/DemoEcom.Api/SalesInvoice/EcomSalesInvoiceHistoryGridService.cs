namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomSalesInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesInvoice;
}
