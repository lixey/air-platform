namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomSupplierReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierReturn;
}
