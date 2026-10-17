using Air.App.Form;
using Air.App.Modules;
using Air.App.Overlay;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.Color;

public sealed class AppColorFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppColor>(db, app)
{
  public override Feature Feature => App.Color;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Handle);
  }
}
