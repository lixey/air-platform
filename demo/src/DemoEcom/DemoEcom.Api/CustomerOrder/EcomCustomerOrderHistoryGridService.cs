namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;
}
