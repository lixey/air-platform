namespace DemoEcom.CustomerOrderFulfillment;

public sealed class EcomCustomerOrderFulfillmentGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine, EcomCustomerOrderFulfillmentRow>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrderFulfillment;

  protected override void SetQuery(Query query)
  {
    query.Where(line => line.Doc.DocTypeId == EcomData.DocType.CustomerOrderId
                  && (line.Doc.DocStatusId == EcomData.DocStatus.CustomerOrderConfirmedId
                      || line.Doc.DocStatusId == EcomData.DocStatus.CustomerOrderPausedId));
    query.Select(line => new EcomCustomerOrderFulfillmentRow
    {
      Id = line.Id,
      Ver = line.Ver,
      Doc = new EcomCustomerOrderFulfillmentRow.DocPart
      {
        Number = line.Doc.Number,
        Date = line.Doc.Date,
        DocStatus = new Reference { Id = line.Doc.DocStatus.Id, Name = line.Doc.DocStatus.Name },
        Organization = new Reference { Id = line.Doc.Organization.Id, Name = line.Doc.Organization.Name },
        Warehouse = line.Doc.Warehouse == null ? null : new Reference { Id = line.Doc.Warehouse.Id, Name = line.Doc.Warehouse.Name },
        Customer = line.Doc.Customer == null ? null : new Reference { Id = line.Doc.Customer.Id, Name = line.Doc.Customer.Name }
      },
      Product = new EcomCustomerOrderFulfillmentRow.ProductPart
      {
        Article = line.Product.Article,
        Name = line.Product.Name,
        Supplier = new Reference { Id = line.Product.Supplier.Id, Name = line.Product.Supplier.Name }
      },
      Qty = line.Qty,
      QtyReserved = line.QtyReserved,
      QtyPurchased = line.QtyPurchased,
      QtyDone = line.QtyDone,
      Short = line.Qty - line.QtyDone - line.QtyReserved > 0m ? line.Qty - line.QtyDone - line.QtyReserved : 0m,
      ToPurchase = line.Qty - line.QtyDone - line.QtyReserved - line.QtyPurchased > 0m
        ? line.Qty - line.QtyDone - line.QtyReserved - line.QtyPurchased
        : 0m
    });

    query.SortByDescending(r => r.ToPurchase).ThenBy(r => r.Doc!.Date).ThenBy(r => r.Doc!.Number);
    query.RecordLabel(e => e.Doc.Number);
    query.NamedFilter(
      "needs-purchase",
      L["Needs a purchase"],
      line => line.Qty - line.QtyDone - line.QtyReserved - line.QtyPurchased > 0m
    ).Badge();
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Doc!.Number);
    columns.Add(r => r.Doc!.Date);
    columns.Add(r => r.Doc!.DocStatus).Label("Status").AsReference<EcomDocStatusGridService, EcomDocStatus>()
      .Where(s => s.DocTypeId, EcomData.DocType.CustomerOrderId)
      .Color(line => line.Doc.DocStatus.Color.Handle);
    columns.Add(r => r.Doc!.Organization).AsReference<OrgOrganizationGridService>().Color(line => line.Doc.Organization.Color.Handle);
    columns.Add(r => r.Doc!.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Doc!.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Product!.Article);
    columns.Add(r => r.Product!.Name).Label("Product");
    columns.Add(r => r.Product!.Supplier).AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    columns.Add(r => r.QtyPurchased).Label("Purchased").Sum();
    columns.Add(r => r.QtyDone).Label("Shipped").Sum();
    columns.Add(r => r.Short).Label("Short").Sum();
    columns.Add(r => r.ToPurchase).Label("To purchase").Sum();
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    _ = EcomDocTexts.Of(L);
    nav.AddAction(CreatePurchaseOrder, b => b.Label(L["Create purchase order"]).Icon(UiIcon.Plus)).AllowReadOnly();
  }

  [RequireRight<EcomPurchaseOrderGridService>(EndpointAction.Edit)]
  public async Task<GridActionDto> CreatePurchaseOrder(GridRequest request, CancellationToken cancellationToken)
  {
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    return await ExecuteBatchAsync(
      request,
      (c, token) => EcomDocActions.PurchaseAsync(
        db,
        c,
        User?.UserId,
        date,
        EcomDocTexts.Of(L),
        token
      ),
      cancellationToken
    );
  }
}
