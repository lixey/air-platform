namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSalesReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetColumns(Columns<EcomDocLineRow> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSalesReturnLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.Price).AsMoney();
    columns.Add(r => r.DiscountPercent).Label("Discount").AsPercent();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
