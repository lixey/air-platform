namespace DemoApp.UserGroup;

public sealed class AppUserGroupGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppUserGroup>(db, app)
{
  public override Feature Feature => App.UserGroup;

  protected override void SetColumns(Columns<AppUserGroup> columns)
  {
    columns.Add(r => r.Id).AsOpenForm<AppUserGroupFormService>();
    columns.Add(r => r.Name).Color(g => g.Color.Handle);
    columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(g => g.Color.Handle);
    columns.Add(r => r.IsActive).AsCheckbox();
  }
}
