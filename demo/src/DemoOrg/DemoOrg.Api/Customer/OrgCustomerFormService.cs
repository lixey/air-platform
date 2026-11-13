namespace DemoOrg.Customer;

public sealed class OrgCustomerFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCustomer>(db, org)
{
  public override Feature Feature => Org.Customer;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Phone).AsPhone("+380 (##) ###-##-##").Span(6);
    Elements.Add(e => e.Email).AsEmail().Span(6);

    Elements.Add(e => e.City).Span(6);
    Elements.Add(e => e.DiscountPercent).Range(0, 100).AsPercent().Span(6);
    Elements.Add(e => e.ManagerId).AsReference<AppUserGridService>().Span(6);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(6);
    Elements.AddSeparator("Delivery and payment");
    Elements.Add(e => e.DeliveryTypeId).AsReference<OrgDeliveryTypeGridService>().Span(4);
    Elements.Add(e => e.PaymentMethodId).AsReference<OrgPaymentMethodGridService>().Span(4);
    Elements.Add(e => e.PaymentTermId).AsReference<OrgPaymentTermGridService>().Span(4);
    Elements.Add(e => e.Note).AsTextarea();
    Elements.AddChild<OrgCustomerContactGridService>().Key("contacts").Label("Contacts");
  }
}
