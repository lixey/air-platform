namespace DemoOrg;

public class OrgService(IUserContext user, IServiceProvider services, ITenantContext tenant, ILocalizer localizer)
  : PlatformAppService(user, services, tenant, localizer);
