namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentFormService(
  EcomDbContext db, EcomIncomingPaymentFlowService flow, EcomService? ecom = null
) : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomDocTypeData.IncomingPaymentId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.IncomingPayment;

  protected override void SetElements()
  {
    Elements.UseRecordLabelOf<EcomIncomingPaymentGridService>();
    Elements.Add(e => e.Number).AsInfo().Span(3);
    Elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    Elements.Disable(Frozen);
    Elements.Add(e => e.Date).Span(3);
    Elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    Elements.Add(e => e.MoneyAccount).AsReference<OrgMoneyAccountGridService>()
      .Where(a => a.OrganizationId, e => e.OrganizationId).Required().Span(3);
    Elements.Add(e => e.Amount).AsMoney().Min(0.01m).Span(3);
    Elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(3);
    Elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(3);
    Elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Span(4);
    Elements.Add(e => e.Contact).AsReference<OrgCustomerContactGridService>()
      .Where(c => c.CustomerId, e => e.CustomerId).Span(4);
    Elements.Add(e => e.CounterpartyId).AsReference<OrgCounterpartyGridService>().Span(4);
    Elements.Add(e => e.Note).AsTextarea();

    Elements.AddChild<EcomIncomingPaymentBasisGridService>().Key("basis").Label("Entered on");
    Elements.AddChild<EcomIncomingPaymentHistoryGridService>().Key("history").Label("History");
  }
}
