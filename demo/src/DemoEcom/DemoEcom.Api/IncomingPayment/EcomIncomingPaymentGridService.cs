namespace DemoEcom.IncomingPayment;

/// <summary>
/// The active tenant's incoming payments, the newest first.
/// </summary>
public sealed class EcomIncomingPaymentGridService(
  EcomDbContext db, EcomIncomingPaymentFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomDocTypeData.IncomingPaymentId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.IncomingPayment;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomIncomingPaymentFormService>();
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
