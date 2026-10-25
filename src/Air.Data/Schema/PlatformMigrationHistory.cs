using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Platform.Data.Schema;

/// <summary>
/// Configures the per-module EF Core migrations history table (<c>__{ModuleHandle}_EFMigrationsHistory</c>)
/// and migrations assembly (<c>{Name}.Migrations</c> for <c>{Name}.Data</c>).
/// </summary>
public static class PlatformMigrationHistory
{
  public static string TableName(string moduleHandle)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleHandle);
    return $"__{moduleHandle}_EFMigrationsHistory";
  }

  public static string? MigrationsAssembly(Type contextType)
  {
    ArgumentNullException.ThrowIfNull(contextType);
    const string dataSuffix = ".Data";
    var name = contextType.Assembly.GetName().Name;
    return name is not null && name.EndsWith(dataSuffix, StringComparison.Ordinal)
      ? name[..^dataSuffix.Length] + ".Migrations"
      : null;
  }

  internal static void Apply(DbContextOptionsBuilder options, string? moduleHandle, Type contextType)
  {
    if (string.IsNullOrWhiteSpace(moduleHandle) ||
        options.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault() is not { } relational)
    {
      return;
    }

    var table = TableName(moduleHandle);
    var assembly = relational.MigrationsAssembly ?? MigrationsAssembly(contextType);
    if (relational.MigrationsHistoryTableName == table && relational.MigrationsAssembly == assembly)
    {
      return;
    }

    var named = relational.WithMigrationsHistoryTableName(table);
    ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(
      assembly is null ? named : named.WithMigrationsAssembly(assembly));
  }
}
