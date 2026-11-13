namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderLineGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocLineGridService<EcomPurchaseOrderFormService>(db, ecom)
{
  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<EcomPurchaseOrderLineFormService>();
    Columns.Add(r => r.SortOrder);
    Columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.QtyDone).Label("Received").Sum();
    Columns.Add(r => r.Price).AsMoney();
    Columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetHooks()
  {
    base.SetHooks();
    Hooks.BeforeDelete((c, cancellationToken) =>
    {
      long[] gone = [.. c.Records.Select(line => line.Id)];
      return EcomDocPurchasing.PurchasedAsync(db, gone, gone, cancellationToken);
    });
  }
}
