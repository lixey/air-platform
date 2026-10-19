namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomSalesReturnFlowService? flow = null)
  : EcomDocHistoryGridService<EcomSalesReturnFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.SalesReturn;
}
