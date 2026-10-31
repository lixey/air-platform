namespace Platform.Agent.Tests.Internals;

internal sealed class TestPlatformAppService(PlatformDbContext db, IUserContext user)
  : PlatformAppService(user, new EmptyServiceProvider(), db.TenantContext, Localizer.Instance);
