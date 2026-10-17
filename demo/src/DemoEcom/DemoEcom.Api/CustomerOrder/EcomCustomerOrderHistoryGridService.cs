using Air.App.Metadata;
using DemoEcom.Data;
using DemoEcom.Documents;

namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderHistoryGridService(
  EcomDbContext db,
  EcomCustomerOrderFlowService flow,
  EcomService? ecom = null
) : EcomDocHistoryGridService<EcomCustomerOrderFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.CustomerOrder;
}
