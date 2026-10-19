namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomSalesInvoiceFlowService? flow = null)
  : EcomDocHistoryGridService<EcomSalesInvoiceFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.SalesInvoice;
}
