namespace DemoApp.User;

public sealed class AppUserGridService(AppDbContext db, AppService app)
  : AppGridService<AppUser>(db, app)
{
  public override Feature Feature => App.User;

  protected override string FiltersWidth => PanelSize.Px(960);

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppUserFormService>();
    Columns.Add(r => r.Login);
    Columns.Add(r => r.Name);
    Columns.Add(r => r.Email);
    Columns.Add(r => r.Tenant).AsReference<AppTenantGridService>();
    Columns.Add(r => r.UserGroup).AsReference<AppUserGroupGridService>().Color(u => u.UserGroup!.Color.Handle);
    Columns.Add(r => r.Language).AsReference<AppLanguageGridService>();
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() => Query.RecordLabel(e => e.Login);

  protected override void SetHooks() =>
    AppUserSecurityRules.AddDeleteHooks(this);

  protected override void SetActions() =>
    AppUserSecurityRules.AddDeactivateAction(this);
}
