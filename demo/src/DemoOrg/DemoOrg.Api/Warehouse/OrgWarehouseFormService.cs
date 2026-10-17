namespace DemoOrg.Warehouse;

public sealed class OrgWarehouseFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgWarehouse>(db, org)
{
  public override Feature Feature => Org.Warehouse;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>();
    elements.Add(e => e.Address);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
