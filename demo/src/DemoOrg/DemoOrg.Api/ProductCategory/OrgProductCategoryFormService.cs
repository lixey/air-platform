namespace DemoOrg.ProductCategory;

public sealed class OrgProductCategoryFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgProductCategory>(db, org)
{
  public override Feature Feature => Org.ProductCategory;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.ColorId).AsReference<AppColorGridService>();
    elements.Add(e => e.SortOrder);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
