namespace DemoApp;

public sealed class AppModule : PlatformModule<App>
{
  protected override void ConfigureServices(WebApplicationBuilder builder)
  {
    builder.AddPlatformPostgres<AppDbContext>();
    builder.Services.AddScoped<IAuthStore, AuthStore<AppUser, AppAuthSession, AppAuthRole, AppAuthGrant, AppAuthAssignment, AppTenant, AppDbContext>>();
  }

  public override ModuleMenu Menu => ModuleMenu
    .Route(App.Tenant)
    .Route(App.User)
    .Route(App.UserGroup)
    .Route(App.Role)
    .Route(App.AgentModel)
    .Route(App.Change)
    .Route(App.Language)
    .Route(App.Color)
    .Build();
}
