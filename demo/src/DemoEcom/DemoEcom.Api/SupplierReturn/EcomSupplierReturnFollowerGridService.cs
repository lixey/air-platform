namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomSupplierReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierReturn;
}
