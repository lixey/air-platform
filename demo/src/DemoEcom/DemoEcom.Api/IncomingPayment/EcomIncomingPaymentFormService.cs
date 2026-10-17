namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.IncomingPaymentId;

  public override Feature Feature => Ecom.IncomingPayment;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(6);
    elements.Add(e => e.MoneyAccountId).AsReference<OrgMoneyAccountGridService>().Required().Span(6);
    elements.Add(e => e.Amount).AsMoney().Required().Span(6);
    elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(6);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomIncomingPaymentFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomIncomingPaymentHistoryGridService>().Key("history").Label("History");
  }
}
