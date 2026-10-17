using DemoEcom.Documents;

namespace DemoEcom.OutgoingPayment;

public sealed class EcomOutgoingPaymentBasisGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocBasisGridService<EcomOutgoingPaymentFormService>(db, ecom)
{
  public override Feature Feature => Ecom.OutgoingPayment;
}
