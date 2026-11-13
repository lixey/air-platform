namespace DemoApp.User;

public sealed class AppUserGridService(AppDbContext db, AppService app)
  : AppGridService<AppUser>(db, app)
{
  public override Feature Feature => App.User;

  protected override string FiltersWidth => PanelSize.Px(960);

  protected override void SetColumns(Columns<AppUser> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppUserFormService>();
    columns.Add(r => r.Login);
    columns.Add(r => r.Name);
    columns.Add(r => r.Email);
    columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    columns.Add(r => r.UserGroup).AsReference<AppUserGroupGridService>().Color(u => u.UserGroup!.Color.Handle);
    columns.Add(r => r.Language).AsReference<AppLanguageGridService>();
    columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery(Query query) => query.RecordLabel(e => e.Login);

  protected override void SetHooks(Hooks hooks) =>
    AppUserSecurityRules.AddDeleteHooks(this, hooks);

  protected override void SetActions(Actions actions) =>
    AppUserSecurityRules.AddDeactivateAction(this, actions);
}
