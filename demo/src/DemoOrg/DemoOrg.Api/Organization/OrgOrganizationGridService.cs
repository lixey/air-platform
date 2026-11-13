namespace DemoOrg.Organization;

public sealed class OrgOrganizationGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgOrganization>(db, org)
{
  public override Feature Feature => Org.Organization;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgOrganizationFormService>();
    Columns.Add(r => r.Name).Color(o => o.Color.Handle);
    Columns.Add(r => r.Code);
    Columns.Add(r => r.TaxId);
    Columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(o => o.Color.Handle);
    Columns.Add(r => r.Phone).AsPhone();
    Columns.Add(r => r.Email).AsEmail();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks()
  {
    const string inUse = "The organization has warehouses or money accounts and cannot be deleted.";
    Hooks.PreventDeleteWhenUsedBy<OrgWarehouse>(w => w.OrganizationId, inUse);
    Hooks.PreventDeleteWhenUsedBy<OrgMoneyAccount>(a => a.OrganizationId, inUse);
  }
}

