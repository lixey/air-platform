namespace DemoApp.Tenant;

public sealed class AppTenantFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppTenant>(db, app)
{
  public override Feature Feature => App.Tenant;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus();
    Elements.Add(e => e.Handle);
    Elements.Add(e => e.ParentId).AsReference<AppTenantGridService>();
    Elements.Add(e => e.TimeZone).AsTimeZone();
    Elements.Add(e => e.IsActive).AsCheckbox();
  }
}

