namespace DemoOrg.Counterparty;

public sealed class OrgCounterpartyFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgCounterparty>(db, org)
{
  public override Feature Feature => Org.Counterparty;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.TaxId);
    elements.Add(e => e.IsSupplier).AsCheckbox();
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
