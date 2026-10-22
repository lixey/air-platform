namespace DemoEcom.IncomingPayment;

/// <summary>
/// The active tenant's incoming payments, the newest first.
/// </summary>
public sealed class EcomIncomingPaymentGridService(
  EcomDbContext db, EcomIncomingPaymentFlowService flow, EcomService? ecom = null
) : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.IncomingPaymentId;

  protected override EcomDocFlowService Flow => flow;

  public override Feature Feature => Ecom.IncomingPayment;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomIncomingPaymentFormService>();
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
