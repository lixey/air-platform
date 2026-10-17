namespace DemoEcom.OutgoingPayment;

public sealed class EcomOutgoingPaymentHistoryGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocHistoryGridService<EcomOutgoingPaymentFormService>(db, ecom)
{
  public override Feature Feature => Ecom.OutgoingPayment;
}
