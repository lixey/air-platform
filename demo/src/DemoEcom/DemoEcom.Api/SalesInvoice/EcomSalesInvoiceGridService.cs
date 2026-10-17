namespace DemoEcom.SalesInvoice;

public sealed class EcomSalesInvoiceGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesInvoiceId;

  public override Feature Feature => Ecom.SalesInvoice;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesInvoiceFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
