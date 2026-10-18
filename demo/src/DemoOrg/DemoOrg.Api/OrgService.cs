using Air.App;
using Air.App.Auth;
using Air.App.Localization;
using Air.Data.Tenancy;

namespace DemoOrg;

public class OrgService(IUserContext user, IServiceProvider services, ITenantContext tenant, ILocalizer localizer)
  : PlatformAppService(user, services, tenant, localizer);
