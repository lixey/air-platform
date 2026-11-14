namespace Platform.Data.SqlServer;

public static class PlatformSqlServerExtensions
{
  public const string DefaultConnectionName = "DefaultConnection";

  public static DbContextOptionsBuilder UsePlatformSqlServer(
    this DbContextOptionsBuilder builder,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
  {
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

    return builder.UseSqlServer(connectionString, o =>
    {
      o.EnableRetryOnFailure(3);
      sqlServer?.Invoke(o);
    });
  }

  public static DbContextOptionsBuilder<TContext> UsePlatformSqlServer<TContext>(
    this DbContextOptionsBuilder<TContext> builder,
    string connectionString,
    Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
    where TContext : DbContext =>
    (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)builder).UsePlatformSqlServer(connectionString, sqlServer);

  public static IHostApplicationBuilder AddPlatformSqlServer<TContext>(
    this IHostApplicationBuilder builder,
    string connectionName = DefaultConnectionName,
    Action<SqlServerDbContextOptionsBuilder>? sqlServer = null)
    where TContext : PlatformDbContext
  {
    ArgumentNullException.ThrowIfNull(builder);

    var connectionString = builder.Configuration.GetConnectionString(connectionName)
      ?? throw new InvalidOperationException($"Connection string '{connectionName}' was not found in configuration.");

    builder.Services.AddDbContext<TContext>(options =>
      options.UsePlatformSqlServer(connectionString, sqlServer));

    builder.Services.RegisterPlatformDbContextHierarchy<TContext>();

    return builder;
  }
}
