namespace DemoOrg.Brand;

public sealed class OrgBrandFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus().Unique("This name is already in use.");
    Elements.Add(e => e.Country).Span(6);
    Elements.Add(e => e.Website).AsUrl().Span(6);
    Elements.Add(e => e.IsActive).AsCheckbox();
  }
}

