namespace Platform.App.Tests.Internals;

internal sealed class TestPlatformAppService(PlatformDbContext db, IUserContext user, IServiceProvider? services = null)
  : PlatformAppService(user, services ?? new EmptyServiceProvider(), db.TenantContext, Localizer.Instance);
