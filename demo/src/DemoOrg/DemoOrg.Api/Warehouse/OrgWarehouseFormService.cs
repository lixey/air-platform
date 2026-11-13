namespace DemoOrg.Warehouse;

public sealed class OrgWarehouseFormService(OrgDbContext db, OrgService? org = null)
  : OrgFormService<OrgWarehouse>(db, org)
{
  public override Feature Feature => Org.Warehouse;

  protected override void SetElements()
  {
    Elements.Add(e => e.OrganizationId).AsReference<OrgOrganizationGridService>().Required().Disabled(!IsNew);
    Elements.Add(e => e.Name).Focus().Unique("The organization already has a warehouse of this name.", e => e.OrganizationId);
    Elements.Add(e => e.Address);
    Elements.Add(e => e.IsActive).AsCheckbox().Span(6);
    Elements.Add(e => e.SortOrder).Span(6);
  }
}

