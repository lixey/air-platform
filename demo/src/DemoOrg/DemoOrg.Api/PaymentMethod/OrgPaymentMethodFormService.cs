namespace DemoOrg.PaymentMethod;

public sealed class OrgPaymentMethodFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgPaymentMethod>(db, org)
{
  public override Feature Feature => Org.PaymentMethod;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Unique("This name is already in use.");
    elements.Add(e => e.IsCash).AsCheckbox().Span(6);
    elements.Add(e => e.IsCashOnDelivery).AsCheckbox().Span(6);
    elements.Add(e => e.IsActive).AsCheckbox().Span(6);
    elements.Add(e => e.SortOrder).Span(6);
  }
}

