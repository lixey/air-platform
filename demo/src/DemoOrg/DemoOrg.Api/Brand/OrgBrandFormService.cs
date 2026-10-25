namespace DemoOrg.Brand;

public sealed class OrgBrandFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus().Unique("This name is already in use.");
    elements.Add(e => e.Country).Span(6);
    elements.Add(e => e.Website).AsUrl().Span(6);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}

