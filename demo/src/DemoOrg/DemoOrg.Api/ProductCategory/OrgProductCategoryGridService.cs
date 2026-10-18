using DemoApp.Color;

namespace DemoOrg.ProductCategory;

public sealed class OrgProductCategoryGridService(OrgDbContext db, OrgService? org = null)
  : OrgGridService<OrgProductCategory>(db, org)
{
  public override Feature Feature => Org.ProductCategory;

  protected override void SetColumns(Columns<OrgProductCategory> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<OrgProductCategoryFormService>();
    columns.Add(r => r.Name).Color(c => c.Color.Handle);
    columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(c => c.Color.Handle);
    columns.Add(r => r.IsActive).AsCheckbox();
    columns.Add(r => r.SortOrder);
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks)
  {
    var inUse = L["The product category is used by products and cannot be deleted."];
    hooks.ValidateDelete(async (c, cancellationToken) =>
    {
      var selected = c.Records.Select(r => (long?)r.Id).ToList();
      var used = await db.OrgProduct.Where(product => selected.Contains(product.CategoryId))
        .Select(product => product.CategoryId!.Value).Distinct().ToListAsync(cancellationToken);
      foreach (var id in used)
      {
        c.AddError("category.used", inUse, id);
      }
    });
  }
}

