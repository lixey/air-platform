namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomPurchaseOrderFlowService? flow = null)
  : EcomDocHistoryGridService<EcomPurchaseOrderFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.PurchaseOrder;
}
