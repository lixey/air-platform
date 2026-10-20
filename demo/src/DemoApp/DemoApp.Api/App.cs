using DemoApp.Change;
using DemoApp.Color;
using DemoApp.Language;
using DemoApp.Role;
using DemoApp.Session;
using DemoApp.Tenant;
using DemoApp.User;
using DemoApp.UserGroup;

namespace DemoApp;

[Module(AppData.ModuleHandle)]
public sealed class App : ModuleDefinition<App>
{
  public static readonly Feature Tenant = Feature<AppTenantGridService>();
  public static readonly Feature User = Feature<AppUserGridService>();
  public static readonly Feature Role = Feature<AppRoleGridService>();
  public static readonly Feature Language = Feature<AppLanguageGridService>().ReadOnly();
  public static readonly Feature Session = Feature<AppSessionGridService>().ReadOnly();
  public static readonly Feature Color = Feature<AppColorGridService>().ReadOnly();
  public static readonly Feature UserGroup = Feature<AppUserGroupGridService>();
  public static readonly Feature Change = Feature<AppChangeGridService>().ReadOnly();
}

