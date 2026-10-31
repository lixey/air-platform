namespace Platform.Api.Tests.Internals;

internal sealed class ApiTestDbContext(DbContextOptions<ApiTestDbContext> options, ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<ApiTestItem> Items => Set<ApiTestItem>();
}
