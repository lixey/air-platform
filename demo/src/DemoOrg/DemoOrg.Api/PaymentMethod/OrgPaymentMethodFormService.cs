namespace DemoOrg.PaymentMethod;

public sealed class OrgPaymentMethodFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgPaymentMethod>(db, org)
{
  public override Feature Feature => Org.PaymentMethod;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.IsCash).AsCheckbox();
    elements.Add(e => e.IsCashOnDelivery).AsCheckbox();
    elements.Add(e => e.SortOrder);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
