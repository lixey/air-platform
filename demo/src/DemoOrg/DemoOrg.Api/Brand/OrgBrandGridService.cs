namespace DemoOrg.Brand;

public sealed class OrgBrandGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgBrand>(db, org)
{
  public override Feature Feature => Org.Brand;

  protected override void SetColumns(Columns<OrgBrand> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgBrandFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Country);
    columns.Add(r => r.Website).AsUrl();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks)
  {
    var inUse = L["The brand is used by products and cannot be deleted."];
    hooks.ValidateDelete(async (c, cancellationToken) =>
    {
      var selected = c.Records.Select(r => r.Id).ToList();
      var used = await db.OrgProduct.Where(product => selected.Contains(product.BrandId))
        .Select(product => product.BrandId).Distinct().ToListAsync(cancellationToken);
      foreach (var id in used)
      {
        c.AddError("brand.used", inUse, id);
      }
    });
  }
}

