using DemoEcom.Data;
using DemoEcom.Documents;
using DemoOrg.Data.Models;

namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentFormService(
  EcomDbContext db, EcomIncomingPaymentFlowService flow, EcomService? ecom = null
) : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.IncomingPaymentId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.IncomingPayment;

  protected override void SetElements(Elements elements)
  {
    elements.UseRecordLabelOf<EcomIncomingPaymentGridService>();
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Disable(Frozen);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew).Span(3);
    elements.Add(e => e.MoneyAccountId).AsReference<OrgMoneyAccountGridService, OrgMoneyAccount>()
      .Where(a => a.OrganizationId, e => e.OrganizationId).Required().Span(3);
    elements.Add(e => e.Amount).AsMoney().Span(3);
    elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(3);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(3);
    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Span(4);
    elements.Add(e => e.ContactId).AsReference<OrgCustomerContactGridService, OrgCustomerContact>()
      .Where(c => c.CustomerId, e => e.CustomerId).Span(4);
    elements.Add(e => e.CounterpartyId).AsReference<OrgCounterpartyGridService>().Span(4);
    elements.Add(e => e.Note).AsTextarea();

    elements.AddChild<EcomIncomingPaymentBasisGridService>().Key("basis").Label("Entered on");
    elements.AddChild<EcomIncomingPaymentHistoryGridService>().Key("history").Label("History");
  }
}
