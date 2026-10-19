namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomIncomingPaymentFlowService? flow = null)
  : EcomDocHistoryGridService<EcomIncomingPaymentFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.IncomingPayment;
}
