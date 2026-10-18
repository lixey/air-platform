namespace DemoOrg.Warehouse;

public sealed class OrgWarehouseGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgWarehouse>(db, org)
{
  public override Feature Feature => Org.Warehouse;

  protected override void SetColumns(Columns<OrgWarehouse> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgWarehouseFormService>();
    columns.Add(r => r.Organization).AsReference<OrgOrganizationGridService>().Color(w => w.Organization.Color.Handle);
    columns.Add(r => r.Name);
    columns.Add(r => r.Address);
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}

