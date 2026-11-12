namespace Platform.Api.Tests.Internals;

[ModuleSchema("Test")]
internal sealed class ApiTestDbContext(DbContextOptions<ApiTestDbContext> options, ITenantContext tenant)
  : PlatformDbContext(options, tenant)
{
  public DbSet<ApiTestItem> Items => Set<ApiTestItem>();
  public DbSet<TestAuthUser> Users => Set<TestAuthUser>();
  public DbSet<TestAuthSession> Sessions => Set<TestAuthSession>();
  public DbSet<TestAuthRole> Roles => Set<TestAuthRole>();
  public DbSet<TestAuthGrant> Grants => Set<TestAuthGrant>();
  public DbSet<TestAuthAssignment> Assignments => Set<TestAuthAssignment>();
  public DbSet<TestAuthTenant> Tenants => Set<TestAuthTenant>();
}
