namespace DemoApp.UserGroup;

public sealed class AppUserGroupFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppUserGroup>(db, app)
{
  public override Feature Feature => App.UserGroup;

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Focus().Unique("This name is already in use.");
    elements.Add(e => e.ColorId).AsReference<AppColorGridService>();
    elements.Add(e => e.Description).AsTextarea();
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
