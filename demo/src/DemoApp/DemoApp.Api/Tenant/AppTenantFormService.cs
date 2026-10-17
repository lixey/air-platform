using Air.App.Form;
using Air.App.Modules;
using Air.App.Overlay;
using DemoApp.Data;
using DemoApp.Data.Models;

namespace DemoApp.Tenant;

public sealed class AppTenantFormService(AppDbContext db, AppService? app = null)
  : AppFormService<AppTenant>(db, app)
{
  public override Feature Feature => App.Tenant;

  protected override OverlayBuilder Presentation(string handle) => OverlayModel.Drawer(handle);

  protected override void SetElements(Elements elements)
  {
    elements.Add(e => e.Name);
    elements.Add(e => e.Handle);
    elements.Add(e => e.ParentId).AsReference<AppTenantGridService>();
    elements.Add(e => e.TimeZone).AsTimeZone();
    elements.Add(e => e.IsActive).AsCheckbox();
  }
}
