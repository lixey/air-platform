namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSupplierReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomSupplierReturnLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
