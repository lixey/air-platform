using Air.App.Form;
using Air.App.Modules;
using Air.App.Overlay;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.Language;

public sealed class AppLanguageFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppLanguage>(db, app)
{
  public override Feature Feature => App.Language;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Handle);
    elements.Add(e => e.Description);
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
