namespace DemoOrg.Warehouse;

public sealed class OrgWarehouseFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgWarehouse>(db, org)
{
  public override Feature Feature => Org.Warehouse;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew);
    elements.Add(e => e.Name).Focus().Unique("The organization already has a warehouse of this name.", e => e.OrganizationId);
    elements.Add(e => e.Address);
    elements.Add(e => e.IsActive).AsCheckbox().Span(6);
    elements.Add(e => e.SortOrder).Span(6);
  }
}

