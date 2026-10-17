namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomSalesReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesReturn;
}
