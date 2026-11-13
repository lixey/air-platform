namespace DemoEcom.SalesReturn;

public sealed class EcomSalesReturnLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSalesReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SalesReturn;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSalesReturnLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.DiscountPercent).Label("Discount").AsPercent();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
