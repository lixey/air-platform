namespace DemoApp.Tenant;

public sealed class AppTenantGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppTenant>(db, app)
{
  public override Feature Feature => App.Tenant;

  protected override void SetColumns(Columns<AppTenant> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppTenantFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Handle);
    columns.Add(r => r.Parent).AsReference<AppTenantGridService>();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);
}
