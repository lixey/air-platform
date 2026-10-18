using Air.App;
using Air.App.Auth;
using Air.App.Localization;
using Air.Data.Tenancy;

namespace DemoApp;

/// <summary>
/// Aggregates ambient application services for DemoApp business services, inheriting from PlatformAppService.
/// </summary>
public class AppService(IUserContext user, IServiceProvider services, ITenantContext tenant, ILocalizer localizer)
  : PlatformAppService(user, services, tenant, localizer);
