namespace DemoEcom.IncomingPayment;

public sealed class EcomIncomingPaymentGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.IncomingPaymentId;

  public override Feature Feature => Ecom.IncomingPayment;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomIncomingPaymentFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
