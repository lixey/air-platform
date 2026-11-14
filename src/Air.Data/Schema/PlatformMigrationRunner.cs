using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Data.Schema;

/// <summary>
/// Shared migration validation, execution, and baselining pipeline across ordered module <see cref="PlatformDbContext"/> instances.
/// </summary>
public static class PlatformMigrationRunner
{
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

  public static async Task MigrateAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Func<PlatformDbContext, string, Func<CancellationToken, Task>, CancellationToken, Task> withSessionLockAsync,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(withSessionLockAsync);
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

    await withSessionLockAsync(
      ordered[0],
      connectionString,
      async ct =>
      {
        foreach (var db in ordered)
        {
          var module = db.ModuleHandle!;
          var knownSet = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
          var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();

          var unknown = applied.Where(m => !knownSet.Contains(m)).ToArray();
          if (unknown.Length > 0)
          {
            throw new InvalidOperationException(
              $"Module '{module}' database history contains unknown migrations not present in this build: [{string.Join(", ", unknown)}].");
          }

          var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
          if (pending.Length > 0)
          {
            log?.Invoke($"[{module}] Applying {pending.Length} migration(s): {string.Join(", ", pending)}...");
            await db.Database.MigrateAsync(ct);
          }
          else
          {
            log?.Invoke($"[{module}] Schema is up to date ({applied.Length} applied migration(s)).");
          }

          await ModuleSchema.VerifyAsync(db, ct);
        }
      },
      cancellationToken);
  }

  public static async Task BaselineAsync(
    IReadOnlyList<PlatformDbContext> contexts,
    Func<PlatformDbContext, string, Func<CancellationToken, Task>, CancellationToken, Task> withSessionLockAsync,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(withSessionLockAsync);
    Check(contexts);
    var ordered = ModuleSchema.OrderContexts(contexts);
    var connectionString = ordered[0].Database.GetConnectionString()
      ?? throw new InvalidOperationException("Database connection string is not configured.");

    await withSessionLockAsync(
      ordered[0],
      connectionString,
      async ct =>
      {
        var productVersion = ProductInfo.GetVersion();
        foreach (var db in ordered)
        {
          var module = db.ModuleHandle!;
          var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
          if (applied.Length > 0)
          {
            log?.Invoke($"[{module}] Already has {applied.Length} applied migration(s) in history; skipping baseline.");
            await ModuleSchema.VerifyAsync(db, ct);
            continue;
          }

          await ModuleSchema.VerifyAsync(db, ct);

          var migrations = db.Database.GetMigrations().ToArray();
          var historyRepo = db.GetService<IHistoryRepository>();
          var createScript = historyRepo.GetCreateIfNotExistsScript();

          var strategy = db.Database.CreateExecutionStrategy();
          await strategy.ExecuteAsync(async () =>
          {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
#pragma warning disable RS0030 // Sanctioned EF Core history repository DDL
            await db.Database.ExecuteSqlRawAsync(createScript, ct);
            foreach (var migrationId in migrations)
            {
              var insertScript = historyRepo.GetInsertScript(new HistoryRow(migrationId, productVersion));
              await db.Database.ExecuteSqlRawAsync(insertScript, ct);
            }
#pragma warning restore RS0030

            await tx.CommitAsync(ct);
          });

          log?.Invoke($"[{module}] Baselined {migrations.Length} migration(s): {string.Join(", ", migrations)}.");
        }
      },
      cancellationToken);
  }
}
