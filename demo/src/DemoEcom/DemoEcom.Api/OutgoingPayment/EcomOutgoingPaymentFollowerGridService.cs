namespace DemoEcom.OutgoingPayment;

public sealed class EcomOutgoingPaymentFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomOutgoingPaymentFormService>(db, ecom)
{
  public override Feature Feature => Ecom.OutgoingPayment;
}
