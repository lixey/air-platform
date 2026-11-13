namespace DemoApp.UserGroup;

public sealed class AppUserGroupFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppUserGroup>(db, app)
{
  public override Feature Feature => App.UserGroup;

  protected override void SetElements()
  {
    Elements.Add(e => e.Name).Focus().Unique("This name is already in use.");
    Elements.Add(e => e.ColorId).AsReference<AppColorGridService>();
    Elements.Add(e => e.Description).AsTextarea();
    Elements.Add(e => e.IsActive).AsCheckbox();
  }
}
