namespace DemoEcom.OutgoingPayment;

public sealed class EcomOutgoingPaymentGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.OutgoingPaymentId;

  public override Feature Feature => Ecom.OutgoingPayment;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomOutgoingPaymentFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Counterparty).Label("Supplier").AsReference<OrgSupplierGridService>();
    columns.Add(r => r.MoneyAccount).AsReference<OrgMoneyAccountGridService>();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
