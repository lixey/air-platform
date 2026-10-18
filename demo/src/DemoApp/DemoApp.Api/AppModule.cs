using Air.App.Metadata;
using Air.App.Modules;
using Air.Data.Postgres;
using DemoApp.Data;
using Microsoft.AspNetCore.Builder;

namespace DemoApp;

public sealed class AppModule : PlatformModule<App>
{
  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<AppDbContext>();
  }

  public override ModuleMenu Menu => ModuleMenu
    .Route(App.Tenant)
    .Route(App.User)
    .Route(App.UserGroup)
    .Route(App.Role)
    .Route(App.Change)
    .Route(App.Language)
    .Route(App.Color)
    .Build();
}
