using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Air.Data.Postgres;

public static class PlatformPostgresExtensions
{
  public const string DefaultConnectionName = "DefaultConnection";

  public static DbContextOptionsBuilder UsePlatformPostgres(
    this DbContextOptionsBuilder builder,
    string connectionString,
    Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
  {
    ArgumentNullException.ThrowIfNull(builder);
    ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

    var utcConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Timezone = "UTC" }.ConnectionString;

    return builder.UseNpgsql(utcConnectionString, o =>
    {
      o.EnableRetryOnFailure(3);
      npgsql?.Invoke(o);
    });
  }

  public static DbContextOptionsBuilder<TContext> UsePlatformPostgres<TContext>(
    this DbContextOptionsBuilder<TContext> builder,
    string connectionString,
    Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
    where TContext : DbContext =>
    (DbContextOptionsBuilder<TContext>)((DbContextOptionsBuilder)builder).UsePlatformPostgres(connectionString, npgsql);

  public static IHostApplicationBuilder AddPlatformPostgres<TContext>(
    this IHostApplicationBuilder builder,
    string connectionName = DefaultConnectionName,
    Action<NpgsqlDbContextOptionsBuilder>? npgsql = null)
    where TContext : PlatformDbContext
  {
    ArgumentNullException.ThrowIfNull(builder);

    var connectionString = builder.Configuration.GetConnectionString(connectionName)
      ?? throw new InvalidOperationException($"Connection string '{connectionName}' was not found in configuration.");

    builder.Services.AddDbContext<TContext>(options =>
      options.UsePlatformPostgres(connectionString, npgsql));

    return builder;
  }
}
