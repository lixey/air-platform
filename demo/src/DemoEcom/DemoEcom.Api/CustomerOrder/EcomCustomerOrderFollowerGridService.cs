namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;
}
