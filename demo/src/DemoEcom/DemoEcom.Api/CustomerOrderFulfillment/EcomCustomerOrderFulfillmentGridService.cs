namespace DemoEcom.CustomerOrderFulfillment;

public sealed class EcomCustomerOrderFulfillmentGridService(EcomDbContext db, EcomService? ecom = null)
  : EcomGridService<EcomDocLine>(db, ecom)
{
  public override Feature Feature => Ecom.CustomerOrderFulfillment;

  protected override void SetQuery()
  {
    Query.Where(line => line.Doc.DocTypeId == EcomDocTypeData.CustomerOrderId
                  && (line.Doc.DocStatusId == EcomDocStatusData.CustomerOrderConfirmedId
                      || line.Doc.DocStatusId == EcomDocStatusData.CustomerOrderPausedId));
    Query.SortByDescending("toPurchase").ThenBy(r => r.Doc.Date).ThenBy(r => r.Doc.Number);
    Query.RecordLabel(e => e.Doc.Number);
    Query.NamedFilter(
      "NeedsPurchase",
      L["Needs a purchase"],
      line => line.Qty - line.QtyDone - line.QtyReserved - line.QtyPurchased > 0m
    );
  }

  protected override void SetColumns()
  {
    Columns.Add(r => r.Doc.Number);
    Columns.Add(r => r.Doc.Date);
    Columns.Add(r => r.Doc.DocStatus).Label("Status").AsReference<EcomDocStatusGridService>()
      .Where(s => s.DocTypeId, EcomDocTypeData.CustomerOrderId)
      .Color(line => line.Doc.DocStatus.Color.Handle);
    Columns.Add(r => r.Doc.Organization).AsReference<OrgOrganizationGridService>().Color(line => line.Doc.Organization.Color.Handle);
    Columns.Add(r => r.Doc.Warehouse).AsReference<OrgWarehouseGridService>();
    Columns.Add(r => r.Doc.Customer).AsReference<OrgCustomerGridService>();
    Columns.Add(r => r.Product.Article);
    Columns.Add(r => r.Product.Name).Label("Product");
    Columns.Add(r => r.Product.Supplier).AsReference<OrgSupplierGridService>();
    Columns.Add(r => r.Qty).Label("Quantity").Sum();
    Columns.Add(r => r.QtyReserved).Label("Reserved").Sum();
    Columns.Add(r => r.QtyPurchased).Label("Purchased").Sum();
    Columns.Add(r => r.QtyDone).Label("Shipped").Sum();
    Columns.Add("short", r => r.Qty - r.QtyDone - r.QtyReserved > 0m ? r.Qty - r.QtyDone - r.QtyReserved : 0m).Label("Short").Sum();
    Columns.Add("toPurchase", r => r.Qty - r.QtyDone - r.QtyReserved - r.QtyPurchased > 0m
      ? r.Qty - r.QtyDone - r.QtyReserved - r.QtyPurchased
      : 0m).Label("To purchase").Sum();
  }

  protected override void SetActions()
  {
    _ = EcomDocTexts.Of(L);
    Actions.Add("CreatePurchaseOrder")
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
