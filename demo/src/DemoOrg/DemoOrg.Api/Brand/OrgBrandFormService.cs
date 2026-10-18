namespace DemoOrg.Brand;

public sealed class OrgBrandFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Unique("This name is already in use.");
    elements.Add(e => e.Country).Span(6);
    elements.Add(e => e.Website).AsUrl().Span(6);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}

