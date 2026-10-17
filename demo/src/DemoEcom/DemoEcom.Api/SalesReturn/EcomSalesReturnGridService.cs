namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocGridService(db, ecom)
{
  protected override long Type => EcomData.DocType.SalesReturnId;

  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetColumns(Columns<EcomDoc> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesReturnFormService>();
    columns.Add(r => r.Number);
    columns.Add(r => r.Date);
    AddStatusColumn(columns);
    columns.Add(r => r.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
