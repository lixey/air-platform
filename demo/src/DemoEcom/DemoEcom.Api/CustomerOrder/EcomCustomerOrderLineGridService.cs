namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomCustomerOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomCustomerOrderLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    columns.Add(r => r.QtyDone).Label("Shipped").Sum();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.DiscountPercent).Label("Discount %").AsPercent().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
