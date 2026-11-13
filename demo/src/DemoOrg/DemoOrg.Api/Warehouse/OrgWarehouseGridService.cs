namespace DemoOrg.Warehouse;

public sealed class OrgWarehouseGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgWarehouse>(db, org)
{
  public override Feature Feature => Org.Warehouse;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgWarehouseFormService>();
    Columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(w => w.Organization.Color.Handle);
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Address);
    Columns.Add(r => r.IsActive).AsCheckbox();
    Columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}

