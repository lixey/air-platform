namespace DemoApp.Tenant;

public sealed class AppTenantGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppTenant>(db, app)
{
  public override Feature Feature => App.Tenant;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppTenantFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Handle);
    Columns.Add(r => r.Parent).AsReference<AppTenantGridService>();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);
}
