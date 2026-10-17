using Air.App.Form;
using Air.App.Modules;
using Air.App.Overlay;
using DemoApp.Color;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.UserGroup;

public sealed class AppUserGroupFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppUserGroup>(db, app)
{
  public override Feature Feature => App.UserGroup;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name).Unique("This name is already in use.");
    elements.Add(e => e.ColorId).AsReference<AppColorGridService>();
    elements.Add(e => e.Description).AsTextarea();
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
