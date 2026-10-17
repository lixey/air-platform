namespace DemoOrg.Brand;

public sealed class OrgBrandFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Country);
    elements.Add(e => e.Website);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
