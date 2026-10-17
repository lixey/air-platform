namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomPurchaseOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.PurchaseOrder;
}
