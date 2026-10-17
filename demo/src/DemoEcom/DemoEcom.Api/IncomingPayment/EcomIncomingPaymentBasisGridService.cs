using DemoEcom.Documents;

namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomIncomingPaymentFormService>(db, ecom)
{
  public override Feature Feature => Ecom.IncomingPayment;
}
