using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace Platform.Data.Internals;

internal static class AtomicMutationCoordinator
{
  private const string RollbackOnlyMessage = "Nested mutation was marked rollback-only.";

  public static List<Type> GetLeafContextTypes(IEnumerable<Type> contextTypes)
  {
    ArgumentNullException.ThrowIfNull(contextTypes);
    var leaves = new List<Type>();
    foreach (var candidate in contextTypes)
    {
      if (candidate == null || !typeof(PlatformDbContext).IsAssignableFrom(candidate))
      {
        continue;
      }

      if (leaves.Any(existing => candidate.IsAssignableFrom(existing)))
      {
        continue;
      }

      leaves.RemoveAll(existing => existing.IsAssignableFrom(candidate));
      leaves.Add(candidate);
    }

    return leaves;
  }

  public static async Task RunAsync(
    IReadOnlyList<PlatformDbContext> rawContexts,
    Func<CancellationToken, Task> work,
    CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(rawContexts);
    ArgumentNullException.ThrowIfNull(work);

    var contexts = new List<PlatformDbContext>(rawContexts.Count);
    foreach (var ctx in rawContexts)
    {
      if (ctx != null && !contexts.Any(existing => ReferenceEquals(existing, ctx)))
      {
        contexts.Add(ctx);
      }
    }

    if (contexts.Count == 0)
    {
      await work(ct);
      return;
    }

    if (contexts.Count == 1)
    {
      await RunSingleAsync(contexts[0], work, ct);
      return;
    }

    await RunSharedAsync(contexts, work, ct);
  }

  private static async Task RunSingleAsync(
    PlatformDbContext db,
    Func<CancellationToken, Task> work,
    CancellationToken ct)
  {
    var isRootRun = !db.IsInMutationRun;
    if (db.Database.CurrentTransaction != null || !db.Database.IsRelational())
    {
      using (db.BeginMutationRun())
      {
        try
        {
          await work(ct);
          if (db.IsMutationRollbackOnly)
          {
            throw new InvalidOperationException(RollbackOnlyMessage);
          }

          await db.FlushMutationJournalAsync(ct);
        }
        catch
        {
          if (isRootRun)
          {
            db.ResetMutationState();
          }

          throw;
        }
      }

      return;
    }

    await NonRetryingExecutionStrategy.RunAsync(
      db,
      async token =>
      {
        await using var tx = await db.Database.BeginTransactionAsync(token);
        using (db.BeginMutationRun())
        {
          try
          {
            await work(token);
            if (db.IsMutationRollbackOnly)
            {
              await tx.RollbackAsync(token);
              throw new InvalidOperationException(RollbackOnlyMessage);
            }

            await db.FlushMutationJournalAsync(token);
            await tx.CommitAsync(token);
          }
          catch
          {
            if (isRootRun)
            {
              db.ResetMutationState();
            }

            throw;
          }
        }
      },
      ct);
  }

  private static async Task RunSharedAsync(
    List<PlatformDbContext> contexts,
    Func<CancellationToken, Task> work,
    CancellationToken ct)
  {
    var primaryDb = contexts.FirstOrDefault(static c => c.Database.IsRelational() && c.Database.CurrentTransaction != null)
      ?? contexts.FirstOrDefault(static c => c.Database.IsRelational());
    if (primaryDb == null)
    {
      await RunNonRelationalGroupAsync(contexts, work, ct);
      return;
    }

    await NonRetryingExecutionStrategy.RunAsync(
      primaryDb,
      token => ExecuteSharedTransactionAsync(primaryDb, contexts, work, token),
      ct);
  }

  private static async Task ExecuteSharedTransactionAsync(
    PlatformDbContext primaryDb,
    List<PlatformDbContext> contexts,
    Func<CancellationToken, Task> work,
    CancellationToken ct)
  {
    var primaryConn = primaryDb.Database.GetDbConnection();
    var primaryConnStr = primaryDb.Database.GetConnectionString();
    var openedPrimaryConnection = false;
    IDbContextTransaction? ownTx = null;
    var sharedSecondaries = new List<PlatformDbContext>();
    var rootRuns = new List<PlatformDbContext>();
    var mutationScopes = new List<IDisposable>(contexts.Count);

    try
    {
      DbTransaction sharedDbTx;
      if (primaryDb.Database.CurrentTransaction is { } existingTx)
      {
        sharedDbTx = existingTx.GetDbTransaction();
      }
      else
      {
        if (primaryConn.State != ConnectionState.Open)
        {
          await primaryConn.OpenAsync(ct);
          openedPrimaryConnection = true;
        }

        ownTx = await primaryDb.Database.BeginTransactionAsync(ct);
        sharedDbTx = ownTx.GetDbTransaction();
      }

      foreach (var secDb in contexts)
      {
        if (ReferenceEquals(secDb, primaryDb) || !secDb.Database.IsRelational() || secDb.Database.CurrentTransaction != null)
        {
          continue;
        }

        var secConn = secDb.Database.GetDbConnection();
        if (!ReferenceEquals(secConn, primaryConn))
        {
          var secConnStr = secDb.Database.GetConnectionString();
          if (!string.IsNullOrEmpty(primaryConnStr) &&
              !string.IsNullOrEmpty(secConnStr) &&
              !string.Equals(secConnStr, primaryConnStr, StringComparison.OrdinalIgnoreCase))
          {
            throw new InvalidOperationException(
              $"Cannot coordinate an atomic transaction across '{primaryDb.GetType().Name}' and '{secDb.GetType().Name}' because they target different connections.");
          }

          secDb.Database.SetDbConnection(primaryConn, contextOwnsConnection: false);
        }

        await secDb.Database.UseTransactionAsync(sharedDbTx, ct);
        sharedSecondaries.Add(secDb);
      }

      foreach (var ctx in contexts)
      {
        if (!ctx.IsInMutationRun)
        {
          rootRuns.Add(ctx);
        }

        mutationScopes.Add(ctx.BeginMutationRun());
      }

      try
      {
        await work(ct);
        if (contexts.Any(static c => c.IsMutationRollbackOnly))
        {
          throw new InvalidOperationException(RollbackOnlyMessage);
        }

        foreach (var ctx in contexts)
        {
          await ctx.FlushMutationJournalAsync(ct);
        }

        if (ownTx != null)
        {
          await ownTx.CommitAsync(ct);
        }
      }
      catch
      {
        if (ownTx != null)
        {
          try
          {
            await ownTx.RollbackAsync(CancellationToken.None);
          }
          catch
          {
          }
        }

        ResetRootRuns(rootRuns);
        throw;
      }
      finally
      {
        for (var i = mutationScopes.Count - 1; i >= 0; i--)
        {
          mutationScopes[i].Dispose();
        }
      }
    }
    finally
    {
      foreach (var secDb in sharedSecondaries)
      {
        try
        {
          await secDb.Database.UseTransactionAsync(null, CancellationToken.None);
        }
        catch
        {
        }
      }

      if (ownTx != null)
      {
        await ownTx.DisposeAsync();
      }

      if (openedPrimaryConnection && primaryConn.State == ConnectionState.Open)
      {
        await primaryConn.CloseAsync();
      }
    }
  }

  private static async Task RunNonRelationalGroupAsync(
    List<PlatformDbContext> contexts,
    Func<CancellationToken, Task> work,
    CancellationToken ct)
  {
    var rootRuns = new List<PlatformDbContext>();
    var scopes = new List<IDisposable>(contexts.Count);
    foreach (var ctx in contexts)
    {
      if (!ctx.IsInMutationRun)
      {
        rootRuns.Add(ctx);
      }

      scopes.Add(ctx.BeginMutationRun());
    }

    try
    {
      await work(ct);
      if (contexts.Any(static c => c.IsMutationRollbackOnly))
      {
        throw new InvalidOperationException(RollbackOnlyMessage);
      }

      foreach (var ctx in contexts)
      {
        await ctx.FlushMutationJournalAsync(ct);
      }
    }
    catch
    {
      ResetRootRuns(rootRuns);
      throw;
    }
    finally
    {
      for (var i = scopes.Count - 1; i >= 0; i--)
      {
        scopes[i].Dispose();
      }
    }
  }

  private static void ResetRootRuns(List<PlatformDbContext> rootRuns)
  {
    foreach (var ctx in rootRuns)
    {
      ctx.ResetMutationState();
    }
  }
}
