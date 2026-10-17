namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentFollowerGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFollowerGridService<EcomIncomingPaymentFormService>(db, ecom)
{
  public override Feature Feature => Ecom.IncomingPayment;
}
