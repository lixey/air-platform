namespace DemoApp.Role;

public sealed class AppRoleGridService(AppDbContext db, AppService app)
  : AppGridService<AppAuthRole>(db, app)
{
  public override Feature Feature => App.Role;

  protected override void SetColumns(Columns<AppAuthRole> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppRoleFormService>();
    columns.Add(r => r.Name);
    columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    columns.Add(r => r.Description);
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Name);

  protected override void SetHooks(Hooks hooks) =>
    AppRoleSecurityRules.AddDeleteHooks<AppAuthRole, AppAuthGrant, AppAuthAssignment>(hooks, db, app, () => User);
}
