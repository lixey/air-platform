namespace DemoEcom.CustomerOrderFulfillment;

public sealed class EcomCustomerOrderFulfillmentGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrderFulfillment;

  protected override void SetColumns(Columns<EcomDocLine> columns)
  {
    columns.Add(r => r.Id);
    columns.Add(r => r.Product).AsReference<OrgProductGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    columns.Add(r => r.QtyPurchased).Label("Purchased").Sum();
    columns.Add(r => r.QtyDone).Label("Shipped").Sum();
  }

  protected override void SetQuery(Query query)
  {
    query.Where(line => line.Doc.DocTypeId == EcomData.DocType.CustomerOrderId);
  }

  protected override void SetNav(Nav nav)
  {
    nav.AddAction(CreatePurchaseOrder, b => b.Label("Create purchase order").Icon(UiIcon.Plus));
  }

  public Task<GridActionDto> CreatePurchaseOrder(GridRequest request, CancellationToken cancellationToken) =>
    ExecuteBatchAsync(request, (context, ct) =>
    {
      if (context.Records.Count > 0)
      {
        var order = new EcomDoc
        {
          DocTypeId = EcomData.DocType.PurchaseOrderId,
          Date = DateOnly.FromDateTime(DateTime.UtcNow),
          Note = "Auto-generated from fulfillment worklist"
        };
        db.Add(order);
      }
      return Task.CompletedTask;
    }, cancellationToken);
}
