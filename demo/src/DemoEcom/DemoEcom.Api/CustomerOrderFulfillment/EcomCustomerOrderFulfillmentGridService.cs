namespace DemoEcom.CustomerOrderFulfillment;

public sealed class EcomCustomerOrderFulfillmentGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrderFulfillment;

  protected override void SetQuery(Query query)
  {
    query.Where(line => line.Doc.DocTypeId == EcomData.DocType.CustomerOrderId
                  && (line.Doc.DocStatusId == EcomData.DocStatus.CustomerOrderConfirmedId
                      || line.Doc.DocStatusId == EcomData.DocStatus.CustomerOrderPausedId));
    query.SortByDescending("toPurchase").ThenBy(r => r.Doc.Date).ThenBy(r => r.Doc.Number);
    query.RecordLabel(e => e.Doc.Number);
    query.NamedFilter(
      "needs-purchase",
      L["Needs a purchase"],
      line => line.Qty - line.QtyDone - line.QtyReserved - line.QtyPurchased > 0m
    );
  }

  protected override void SetColumns(Columns columns)
  {
    columns.Add(r => r.Doc.Number);
    columns.Add(r => r.Doc.Date);
    columns.Add(r => r.Doc.DocStatus).Label("Status").AsReference<EcomDocStatusGridService>()
      .Where(s => s.DocTypeId, EcomData.DocType.CustomerOrderId)
      .Color(line => line.Doc.DocStatus.Color.Handle);
    columns.Add(r => r.Doc.Organization).AsReference<OrgOrganizationGridService>().Color(line => line.Doc.Organization.Color.Handle);
    columns.Add(r => r.Doc.Warehouse).AsReference<OrgWarehouseGridService>();
    columns.Add(r => r.Doc.Customer).AsReference<OrgCustomerGridService>();
    columns.Add(r => r.Product.Article);
    columns.Add(r => r.Product.Name).Label("Product");
    columns.Add(r => r.Product.Supplier).AsReference<OrgSupplierGridService>();
    columns.Add(r => r.Qty).Label("Quantity").Sum();
    columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    columns.Add(r => r.QtyPurchased).Label("Purchased").Sum();
    columns.Add(r => r.QtyDone).Label("Shipped").Sum();
    columns.Add("short", r => r.Qty - r.QtyDone - r.QtyReserved > 0m ? r.Qty - r.QtyDone - r.QtyReserved : 0m).Label("Short").Sum();
    columns.Add("toPurchase", r => r.Qty - r.QtyDone - r.QtyReserved - r.QtyPurchased > 0m
      ? r.Qty - r.QtyDone - r.QtyReserved - r.QtyPurchased
      : 0m).Label("To purchase").Sum();
  }

  protected override void SetActions(Actions actions)
  {
    _ = EcomDocTexts.Of(L);
    actions.Add("CreatePurchaseOrder")
      .Allow(Ecom.PurchaseOrder.Edit)
      .Label("Create purchase order")
      .Icon(UiIcon.Plus)
      .AllowReadOnly()
      .Do((ctx, ct) => EcomDocActions.PurchaseAsync(
        db,
        ctx,
        User?.UserId,
        Time.Today,
        EcomDocTexts.Of(L),
        ct));
  }
}
