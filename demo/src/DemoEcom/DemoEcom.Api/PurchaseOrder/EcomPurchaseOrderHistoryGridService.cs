namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomPurchaseOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.PurchaseOrder;
}
