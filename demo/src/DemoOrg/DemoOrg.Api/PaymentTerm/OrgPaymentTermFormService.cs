namespace DemoOrg.PaymentTerm;

public sealed class OrgPaymentTermFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgPaymentTerm>(db, org)
{
  public override Feature Feature => Org.PaymentTerm;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Handle);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
