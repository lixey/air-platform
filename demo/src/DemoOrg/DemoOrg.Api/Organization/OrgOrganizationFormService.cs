namespace DemoOrg.Organization;

public sealed class OrgOrganizationFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgOrganization>(db, org)
{
  public override Feature Feature => Org.Organization;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Code);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
