namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomSupplierReturnFlowService? flow = null)
  : EcomDocHistoryGridService<EcomSupplierReturnFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.SupplierReturn;
}
