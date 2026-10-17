namespace DemoEcom.SupplierReturn;

public sealed class EcomSupplierReturnLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomSupplierReturnFormService>(db, ecom)
{
  public override Feature Feature => Ecom.SupplierReturn;

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomSupplierReturnLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
