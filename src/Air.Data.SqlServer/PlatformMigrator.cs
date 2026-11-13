using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Data.SqlServer;

/// <summary>
/// Orchestrates validation, migration, and baselining across ordered module <see cref="PlatformDbContext"/> instances on SQL Server.
/// </summary>
public static class PlatformMigrator
{
  private const string MigrationLockResource = "platform-migrations";
  private const int MigrationLockTimeoutMs = 600_000;
  private const int MigrationLockCommandTimeoutSeconds = 610;

  /// <summary>
  /// Validates cross-module schema ownership, ensures every module has migrations, and checks for pending model changes.
  /// </summary>
  public static void Check(IReadOnlyList<PlatformDbContext> contexts)
  {
    ModuleSchema.Validate(contexts);

    foreach (var db in ModuleSchema.OrderContexts(contexts))
    {
      var module = db.ModuleHandle!;
      var migrations = db.Database.GetMigrations().ToArray();
      if (migrations.Length == 0)
      {
        throw new InvalidOperationException(
          $"Module '{module}' ({db.GetType().Name}) has no EF Core migrations in its migrations assembly.");
      }

      if (db.Database.HasPendingModelChanges())
      {
        throw new InvalidOperationException(
          $"Module '{module}' ({db.GetType().Name}) has pending model changes that are not captured in a migration.");
      }
    }
  }

  /// <summary>
  /// Applies all pending module migrations in inheritance order under a SQL Server session application lock and verifies schema integrity.
  /// </summary>
  public static async Task MigrateAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    Check(contexts);
    var ordered = ModuleSchema.OrderContexts(contexts);

    var firstCreator = ordered[0].GetService<IRelationalDatabaseCreator>();
    if (!await firstCreator.ExistsAsync(cancellationToken))
    {
      log?.Invoke("Target database does not exist yet; creating database...");
      await firstCreator.CreateAsync(cancellationToken);
    }

    var connectionString = ordered[0].Database.GetConnectionString()
      ?? throw new InvalidOperationException("Database connection string is not configured.");

    await using var lockConn = new SqlConnection(connectionString);
    await ordered[0].Database.CreateExecutionStrategy().ExecuteAsync(
      async ct => await lockConn.OpenAsync(ct),
      cancellationToken);
    await AcquireMigrationLockAsync(lockConn, cancellationToken);
    try
    {
      foreach (var db in ordered)
      {
        var module = db.ModuleHandle!;
        var known = db.Database.GetMigrations().ToArray();
        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();

        var unknown = applied.Where(m => !knownSet.Contains(m)).ToArray();
        if (unknown.Length > 0)
        {
          throw new InvalidOperationException(
            $"Module '{module}' database history contains unknown migrations not present in this build: [{string.Join(", ", unknown)}].");
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length > 0)
        {
          log?.Invoke($"[{module}] Applying {pending.Length} migration(s): {string.Join(", ", pending)}...");
          await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
          log?.Invoke($"[{module}] Schema is up to date ({applied.Length} applied migration(s)).");
        }

        await ModuleSchema.VerifyAsync(db, cancellationToken);
      }
    }
    finally
    {
      await ReleaseMigrationLockAsync(lockConn);
    }
  }

  /// <summary>
  /// Baselines an existing database (previously created via EnsureCreated) by verifying all module tables/columns
  /// and recording each module's migrations in its <c>__{Module}_EFMigrationsHistory</c> table without running DDL.
  /// </summary>
  public static async Task BaselineAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    Check(contexts);
    var ordered = ModuleSchema.OrderContexts(contexts);
    var connectionString = ordered[0].Database.GetConnectionString()
      ?? throw new InvalidOperationException("Database connection string is not configured.");

    await using var lockConn = new SqlConnection(connectionString);
    await ordered[0].Database.CreateExecutionStrategy().ExecuteAsync(
      async ct => await lockConn.OpenAsync(ct),
      cancellationToken);
    await AcquireMigrationLockAsync(lockConn, cancellationToken);
    try
    {
      var productVersion = ProductInfo.GetVersion();
      foreach (var db in ordered)
      {
        var module = db.ModuleHandle!;
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (applied.Length > 0)
        {
          log?.Invoke($"[{module}] Already has {applied.Length} applied migration(s) in history; skipping baseline.");
          await ModuleSchema.VerifyAsync(db, cancellationToken);
          continue;
        }

        await ModuleSchema.VerifyAsync(db, cancellationToken);

        var migrations = db.Database.GetMigrations().ToArray();
        var historyRepo = db.GetService<IHistoryRepository>();
        var createScript = historyRepo.GetCreateIfNotExistsScript();

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
          await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
#pragma warning disable RS0030 // Sanctioned EF Core history repository DDL
          await db.Database.ExecuteSqlRawAsync(createScript, cancellationToken);
          foreach (var migrationId in migrations)
          {
            var insertScript = historyRepo.GetInsertScript(new HistoryRow(migrationId, productVersion));
            await db.Database.ExecuteSqlRawAsync(insertScript, cancellationToken);
          }
#pragma warning restore RS0030

          await tx.CommitAsync(cancellationToken);
        });

        log?.Invoke($"[{module}] Baselined {migrations.Length} migration(s): {string.Join(", ", migrations)}.");
      }
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
      // Connection closing releases session application locks automatically
    }
  }
}
