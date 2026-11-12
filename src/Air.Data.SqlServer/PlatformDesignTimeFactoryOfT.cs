using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Data.SqlServer;

/// <summary>
/// Base design-time factory for module <see cref="PlatformDbContext"/> types used by EF Core CLI tooling on SQL Server.
/// </summary>
public abstract class PlatformDesignTimeFactory<TDbContext> : IDesignTimeDbContextFactory<TDbContext>
  where TDbContext : PlatformDbContext
{
  private const string OfflineConnectionString =
    "Server=localhost;Database=platform_design_time;Trusted_Connection=True;TrustServerCertificate=True";

  public TDbContext CreateDbContext(string[] args)
  {
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
      ?? OfflineConnectionString;

    var options = new DbContextOptionsBuilder<TDbContext>()
      .UsePlatformSqlServer(connectionString)
      .Options;

    return CreateContext(options);
  }

  protected virtual TDbContext CreateContext(DbContextOptions<TDbContext> options) =>
    typeof(TDbContext).GetConstructor([typeof(DbContextOptions<TDbContext>), typeof(ITenantContext)]) is not null
      ? (TDbContext)Activator.CreateInstance(typeof(TDbContext), options, null)!
      : (TDbContext)Activator.CreateInstance(typeof(TDbContext), options)!;
}
