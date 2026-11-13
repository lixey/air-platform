namespace DemoApp.Role;

public sealed class AppRoleUserGridService(AppDbContext db, AppService? app = null)
  : AppGridService<AppAuthAssignment>(db, app)
{
  public override Feature Feature => App.Role;
  protected override bool IsReadOnly => true;

  protected override void SetParent() => Parent.Bind<AppRoleFormService>(e => e.AuthRoleId);

  protected override void SetColumns()
  {
    Columns.Add(r => r.User.Login);
    Columns.Add(r => r.User.Name);
    Columns.Add(r => r.IsActive).AsCheckbox();
  }

  protected override void SetQuery() =>
    Query.Where(assignment => db.AppUser.Any(user => user.Id == assignment.UserId));
}
