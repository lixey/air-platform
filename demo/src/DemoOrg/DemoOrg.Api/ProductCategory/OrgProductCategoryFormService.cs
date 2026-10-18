namespace DemoOrg.ProductCategory;

public sealed class OrgProductCategoryFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgProductCategory>(db, org)
{
  public override Feature Feature => Org.ProductCategory;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Unique("This name is already in use.");
    elements.Add(e => e.ColorId).AsReference<AppColorGridService>().Span(4);
    elements.Add(e => e.IsActive).AsCheckbox().Span(4);
    elements.Add(e => e.SortOrder).Span(4);
  }
}

