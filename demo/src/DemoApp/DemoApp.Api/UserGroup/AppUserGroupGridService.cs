namespace DemoApp.UserGroup;

public sealed class AppUserGroupGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppUserGroup>(db, app)
{
  public override Feature Feature => App.UserGroup;

  protected override void SetColumns()
  {
    Columns.Add(r => r.Id).AsOpenForm<AppUserGroupFormService>();
    Columns.Add(r => r.Name).Color(g => g.Color.Handle);
    Columns.Add(r => r.Color).AsReference<AppColorGridService>().Color(g => g.Color.Handle);
    Columns.Add(r => r.IsActive).AsCheckbox();
  }
}
