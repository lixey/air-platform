namespace DemoOrg.Brand;

public sealed class OrgBrandGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<OrgBrandFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Country);
    Columns.Add(r => r.Website).AsUrl();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks() =>
    Hooks.PreventDeleteWhenUsedBy<OrgProduct>(p => p.BrandId, "The brand is used by products and cannot be deleted.");
}

