namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomSalesInvoiceFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesInvoice;
}
