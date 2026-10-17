namespace DemoOrg.Customer;

public sealed class OrgCustomerFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCustomer>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Phone).AsPhone().Span(6);
    elements.Add(e => e.Email).AsEmail().Span(6);
    elements.Add(e => e.City).Span(6);
    elements.Add(e => e.DiscountPercent).Range(0, 100).AsPercent().Span(6);
    elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    elements.Add(e => e.IsActive).AsCheckbox().Span(6);
    elements.AddSeparator("Delivery and payment");
    elements.Add(e => e.DeliveryTypeId).AsReference<OrgDeliveryTypeGridService>().Span(4);
    elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(4);
    elements.Add(e => e.PaymentTermId).AsReference<OrgPaymentTermGridService>().Span(4);
    elements.Add(e => e.Note).AsTextarea();
    elements.AddChild<OrgCustomerContactGridService>().Key("contacts").Label("Contacts");
  }
}
