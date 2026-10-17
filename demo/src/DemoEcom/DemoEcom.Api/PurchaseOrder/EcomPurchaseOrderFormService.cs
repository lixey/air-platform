namespace DemoEcom.PurchaseOrder;

public sealed class EcomPurchaseOrderFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.PurchaseOrderId;

  public override Feature Feature => Ecom.PurchaseOrder;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);
    elements.Add(e => e.CounterpartyId).Label("Supplier").AsReference<OrgSupplierGridService>().Required().Span(6);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService>().Required().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.DueDate).Span(6);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomPurchaseOrderLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomPurchaseOrderFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomPurchaseOrderBasisGridService>().Key("basis").Label("Based on");
    elements.AddChild<EcomPurchaseOrderHistoryGridService>().Key("history").Label("History");
  }
}
