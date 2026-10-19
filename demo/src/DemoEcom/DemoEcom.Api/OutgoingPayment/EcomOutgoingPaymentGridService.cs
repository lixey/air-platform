using Air.App.Grid;
using DemoEcom.Data;
using DemoEcom.Documents;

namespace DemoEcom.OutgoingPayment;

/// <summary>
/// The active tenant's outgoing payments, the newest first.
/// </summary>
public sealed class EcomOutgoingPaymentGridService(
  EcomDbContext db, EcomOutgoingPaymentFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.OutgoingPaymentId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.OutgoingPayment;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomOutgoingPaymentFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Counterparty).AsReference<OrgCounterpartyGridService>();
    columns.Add(r => r.Amount).AsMoney();
  }
}
