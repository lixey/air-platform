namespace DemoOrg.Product;

public sealed class OrgProductFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgProduct>(db, org)
{
  public override Feature Feature => Org.Product;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Article);
    elements.Add(e => e.BrandId).AsReference<OrgBrandGridService>();
    elements.Add(e => e.CategoryId).AsReference<OrgProductCategoryGridService>();
    elements.Add(e => e.UnitId).AsReference<OrgUnitGridService>();
    elements.Add(e => e.Price);
    elements.Add(e => e.Description).AsTextarea();
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
