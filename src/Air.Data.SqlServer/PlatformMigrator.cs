namespace Platform.Data.SqlServer;

/// <summary>
/// Orchestrates validation, migration, and baselining across ordered module <see cref="PlatformDbContext"/> instances on SQL Server.
/// </summary>
public static class PlatformMigrator
{
  private const string MigrationLockResource = "platform-migrations";
  private const int MigrationLockTimeoutMs = 600_000;
  private const int MigrationLockCommandTimeoutSeconds = 610;

  public static void Check(IReadOnlyList<PlatformDbContext> contexts) =>
    PlatformMigrationRunner.Check(contexts);

  public static Task MigrateAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default) =>
    PlatformMigrationRunner.MigrateAsync(contexts, WithMigrationLockAsync, log, cancellationToken);

  public static Task BaselineAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default) =>
    PlatformMigrationRunner.BaselineAsync(contexts, WithMigrationLockAsync, log, cancellationToken);

  private static async Task WithMigrationLockAsync(
    PlatformDbContext firstDb,
    string connectionString,
    Func<CancellationToken, Task> action,
    CancellationToken cancellationToken)
  {
    await using var lockConn = new SqlConnection(connectionString);
    await firstDb.Database.CreateExecutionStrategy().ExecuteAsync(
      async ct => await lockConn.OpenAsync(ct),
      cancellationToken);
    await AcquireMigrationLockAsync(lockConn, cancellationToken);
    try
    {
      await action(cancellationToken);
    }
    finally
    {
      await ReleaseMigrationLockAsync(lockConn);
    }
  }

  private static async Task AcquireMigrationLockAsync(SqlConnection conn, CancellationToken ct)
  {
    await using var cmd = new SqlCommand(
      """
      DECLARE @res int;
      EXEC @res = sp_getapplock
        @Resource = @resource,
        @LockMode = 'Exclusive',
        @LockOwner = 'Session',
        @LockTimeout = @timeout;
      IF @res < 0
        THROW 51000, 'Failed to acquire platform-migrations lock.', 1;
      """,
      conn)
    {
      CommandTimeout = MigrationLockCommandTimeoutSeconds
    };
    cmd.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = MigrationLockResource });
    cmd.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int) { Value = MigrationLockTimeoutMs });
    await cmd.ExecuteNonQueryAsync(ct);
  }

  private static async Task ReleaseMigrationLockAsync(SqlConnection conn)
  {
    try
    {
      await using var cmd = new SqlCommand(
        "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';",
        conn);
      cmd.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = MigrationLockResource });
      await cmd.ExecuteNonQueryAsync();
    }
    catch
    {
      // Closing the dedicated session connection releases sp_getapplock automatically.
    }
  }
}
