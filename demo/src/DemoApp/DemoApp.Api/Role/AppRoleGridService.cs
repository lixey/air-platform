namespace DemoApp.Role;

public sealed class AppRoleGridService(AppDbContext db, AppService app)
  : AppGridService<AppAuthRole>(db, app)
{
  public override Feature Feature => App.Role;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppRoleFormService>();
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    Columns.Add(r => r.Description);
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Name);

  protected override void SetHooks() =>
    AppRoleSecurityRules.AddDeleteHooks<AppAuthRole, AppAuthGrant, AppAuthAssignment>(this);
}
