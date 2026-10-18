using Air.App;
using Air.App.Auth;
using Air.App.Localization;
using Air.Data.Tenancy;

namespace DemoEcom;

public class EcomService(IUserContext user, IServiceProvider services, ITenantContext tenant, ILocalizer localizer)
  : PlatformAppService(user, services, tenant, localizer);
