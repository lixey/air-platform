using Air.App.Metadata;
using Air.App.Modules;

namespace DemoApp;

public sealed class AppModule : PlatformModule<App>
{
  public override ModuleMenu Menu => ModuleMenu
    .Route(App.Tenant)
    .Route(App.User)
    .Route(App.UserGroup)
    .Route(App.Role)
    .Route(App.Language)
    .Route(App.Color)
    .Build();
}
