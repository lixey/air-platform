namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomSalesReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesReturn;
}
