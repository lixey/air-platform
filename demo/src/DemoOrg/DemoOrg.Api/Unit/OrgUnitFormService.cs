namespace DemoOrg.Unit;

public sealed class OrgUnitFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgUnit>(db, org)
{
  public override Feature Feature => Org.Unit;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus();
    elements.Add(e => e.Handle);
  }
}
