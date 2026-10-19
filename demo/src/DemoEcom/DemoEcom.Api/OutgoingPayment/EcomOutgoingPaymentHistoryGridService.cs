namespace DemoEcom.OutgoingPayment;

public sealed class EcomOutgoingPaymentHistoryGridService(EcomDbContext db, EcomService? ecom = null, EcomOutgoingPaymentFlowService? flow = null)
  : EcomDocHistoryGridService<EcomOutgoingPaymentFormService>(db, ecom, flow)
{
  public override Feature Feature => Ecom.OutgoingPayment;
}
