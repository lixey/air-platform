using DemoEcom.Documents;

namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomPurchaseOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetColumns(Columns<EcomDocLineRow> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<EcomPurchaseOrderLineFormService>();
    columns.Add(r => r.SortOrder);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum().Editable();
    columns.Add(r => r.Price).AsMoney().Editable();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }
}
