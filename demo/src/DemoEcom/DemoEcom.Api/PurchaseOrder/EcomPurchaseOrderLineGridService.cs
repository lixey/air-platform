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
    columns.Add(r => r.Article);
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.QtyDone).Label("Received").Sum();
    columns.Add(r => r.Price).AsMoney();
    columns.Add(r => r.Amount).AsMoney().Sum();
  }

  protected override void SetHooks(Hooks hooks)
  {
    base.SetHooks(hooks);
    hooks.BeforeDelete((c, cancellationToken) =>
    {
      long[] gone = [.. c.Records.Select(line => line.Id)];
      return EcomDocEntering.PurchasedAsync(db, gone, gone, cancellationToken);
    });
  }
}
