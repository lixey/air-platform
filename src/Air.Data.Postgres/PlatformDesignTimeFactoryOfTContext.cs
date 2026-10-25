using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Data.Postgres;

/// <summary>
/// Base design-time factory for module <see cref="PlatformDbContext"/> types used by EF Core CLI tooling.
/// </summary>
public abstract class PlatformDesignTimeFactory<TContext> : IDesignTimeDbContextFactory<TContext>
  where TContext : PlatformDbContext
{
  private const string OfflineConnectionString = "Host=localhost;Port=5432;Database=air_design_time;Username=postgres";

  public TContext CreateDbContext(string[] args)
  {
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
      ?? OfflineConnectionString;

    var options = new DbContextOptionsBuilder<TContext>()
      .UsePlatformPostgres(connectionString)
      .Options;

    return CreateContext(options);
  }

  protected virtual TContext CreateContext(DbContextOptions<TContext> options) =>
    (TContext)Activator.CreateInstance(typeof(TContext), options, null)!;
}
