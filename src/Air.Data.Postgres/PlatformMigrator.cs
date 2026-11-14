namespace Platform.Data.Postgres;

/// <summary>
/// Orchestrates validation, migration, and baselining across ordered module <see cref="PlatformDbContext"/> instances on PostgreSQL.
/// </summary>
public static class PlatformMigrator
{
  private const long MigrationAdvisoryLockId = 7283945102938471L;

  public static void Check(IReadOnlyList<PlatformDbContext> contexts) =>
    PlatformMigrationRunner.Check(contexts);

  public static Task MigrateAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default) =>
    PlatformMigrationRunner.MigrateAsync(contexts, WithAdvisoryLockAsync, log, cancellationToken);

  public static Task BaselineAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default) =>
    PlatformMigrationRunner.BaselineAsync(contexts, WithAdvisoryLockAsync, log, cancellationToken);

  private static async Task WithAdvisoryLockAsync(
    PlatformDbContext _,
    string connectionString,
    Func<CancellationToken, Task> action,
    CancellationToken cancellationToken)
  {
    // Open dedicated session connection directly outside retry strategy so pg_advisory_lock is never silently dropped.
    await using var lockConn = new NpgsqlConnection(connectionString);
    await lockConn.OpenAsync(cancellationToken);
    await using (var acquireCmd = new NpgsqlCommand($"SELECT pg_advisory_lock({MigrationAdvisoryLockId});", lockConn))
    {
      await acquireCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    try
    {
      await action(cancellationToken);
    }
    finally
    {
      try
      {
        await using var releaseCmd = new NpgsqlCommand($"SELECT pg_advisory_unlock({MigrationAdvisoryLockId});", lockConn);
        await releaseCmd.ExecuteNonQueryAsync();
      }
      catch
      {
        // Closing the dedicated session connection releases pg_advisory_lock automatically.
      }
    }
  }
}
