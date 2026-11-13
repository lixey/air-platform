namespace DemoEcom.OutgoingPayment;

/// <summary>
/// The active tenant's outgoing payments, the newest first.
/// </summary>
public sealed class EcomOutgoingPaymentGridService(
  EcomDbContext db, EcomOutgoingPaymentFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.OutgoingPaymentId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.OutgoingPayment;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomOutgoingPaymentFormService>();
    Columns.Add(r => r.Number);
    Columns.Add(r => r.Date);
    AddStatusColumn();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(doc => doc.Organization.Color.Handle);
    Columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    Columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.Counterparty).AsReference<OrgCounterpartyGridService>();
    Columns.Add(r => r.Amount).AsMoney();
  }
}
