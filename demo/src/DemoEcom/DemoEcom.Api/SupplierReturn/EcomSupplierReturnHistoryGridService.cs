namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomSupplierReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierReturn;
}
