namespace Platform.App.Tests.Internals;

internal sealed class MockParallelDbContext(
  DbContextOptions<MockParallelDbContext> options,
  ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<TestTag> Tags => Set<TestTag>();
}
