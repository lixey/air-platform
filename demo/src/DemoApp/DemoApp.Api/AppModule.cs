using Air.App.Auth;
using Air.App.Metadata;
using Air.App.Modules;
using Air.Data.Postgres;
using DemoApp.Data;
using DemoApp.Data.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DemoApp;

public sealed class AppModule : PlatformModule<App>
{
  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<AppDbContext>();
    builder.Services.AddScoped<IAuthStore, AuthStore<AppUser, AppAuthSession, AppAuthRole, AppAuthGrant, AppAuthAssignment, AppDbContext>>();
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
