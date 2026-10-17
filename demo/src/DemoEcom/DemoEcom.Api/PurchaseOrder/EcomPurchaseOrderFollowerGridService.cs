namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomPurchaseOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.PurchaseOrder;
}
