namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomIncomingPaymentFormService>(db, ecom)
{
  public override Feature Feature => Ecom.IncomingPayment;
}
