namespace DemoEcom.CustomerOrder;

public sealed class EcomCustomerOrderFormService(EcomDbContext db, EcomService? ecom = null)
  : EcomDocFormService(db, ecom)
{
  protected override long Type => EcomData.DocType.CustomerOrderId;

  public override Feature Feature => Ecom.CustomerOrder;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Number).AsInfo().Span(3);
    elements.Add(e => e.DocStatusId).Label("Status").Span(3);
    elements.Add(e => e.Date).Span(3);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Span(3);

    elements.Add(e => e.CustomerId).AsReference<OrgCustomerGridService>().Required().Span(4);
    elements.Add(e => e.WarehouseId).AsReference<OrgWarehouseGridService>().Required().Span(4);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(4);

    elements.AddSeparator("Delivery and payment");
    elements.Add(e => e.DeliveryTypeId).AsReference<OrgDeliveryTypeGridService>().Span(4);
    elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(4);
    elements.Add(e => e.PaymentTermId).AsReference<OrgPaymentTermGridService>().Span(4);

    elements.Add(e => e.DeliveryAddress).Span(12);
    elements.Add(e => e.Note).AsTextarea().Span(12);

    elements.AddChild<EcomCustomerOrderLineGridService>().Key("lines").Label("Lines");
    elements.AddChild<EcomCustomerOrderFollowerGridService>().Key("followers").Label("Followed by");
    elements.AddChild<EcomCustomerOrderHistoryGridService>().Key("history").Label("History");
  }
}
