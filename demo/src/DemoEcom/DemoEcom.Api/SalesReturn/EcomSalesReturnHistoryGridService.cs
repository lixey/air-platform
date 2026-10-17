namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomSalesReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesReturn;
}
