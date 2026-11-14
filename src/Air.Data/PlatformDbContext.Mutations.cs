using Microsoft.EntityFrameworkCore.Storage;
using Platform.Data.Internals;

namespace Platform.Data;

public abstract partial class PlatformDbContext
{
  private MutationRunScope? _activeMutationRun;
  private bool _suppressDerivedAudit;
  private bool _suppressAllAudit;

  public bool IsInMutationRun => _activeMutationRun != null;

  public bool IsMutationRollbackOnly => _activeMutationRun?.IsRollbackOnly == true;

  public void MarkMutationRollbackOnly()
  {
    _activeMutationRun?.IsRollbackOnly = true;
  }

  public Guid? CurrentMutationRunId => _activeMutationRun?.RunId;

  public IDisposable BeginMutationRun()
  {
    if (_activeMutationRun != null)
    {
      var parentRun = _activeMutationRun;
      parentRun.Depth++;
      var nestedDisposed = 0;
      return new DisposableScope(() =>
      {
        if (Interlocked.Exchange(ref nestedDisposed, 1) == 0 &&
            ReferenceEquals(_activeMutationRun, parentRun) &&
            --parentRun.Depth <= 0)
        {
          _activeMutationRun = null;
        }
      });
    }

    MutationRunScope? scope = null;
    var rootDisposed = 0;
    scope = new MutationRunScope(() =>
    {
      if (Interlocked.Exchange(ref rootDisposed, 1) == 0 &&
          scope is not null &&
          ReferenceEquals(_activeMutationRun, scope) &&
          --scope.Depth <= 0)
      {
        _activeMutationRun = null;
      }
    });
    _activeMutationRun = scope;
    return scope;
  }

  public void ResetMutationState()
  {
    _activeMutationRun = null;
    ChangeTracker.Clear();
  }

  public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
    throw new NotSupportedException("Synchronous SaveChanges is disabled in PlatformDbContext. Use SaveChangesAsync instead.");

  public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ChangeTracker.DetectChanges();
    await CascadeLoader.LoadDependentsAsync(this, cancellationToken);

    await EntityWriteConventions.ApplyAsync(this, ChangeTracker, TenantContext, _activeMutationRun, cancellationToken);

    if (_activeMutationRun != null)
    {
      var pending = ChangeJournalRecorder.CaptureRunFlushStates(
        ChangeTracker, TenantContext, _activeMutationRun, _suppressAllAudit, _suppressDerivedAudit);
      var res = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
      ChangeJournalRecorder.CompleteRunFlushStates(pending);
      return res;
    }

    if (_suppressAllAudit || !ChangeTracker.Entries()
          .Any(e => ChangeJournalRecorder.IsJournaled(e, _suppressAllAudit, _suppressDerivedAudit) &&
                    e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
    {
      return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    if (Database.CurrentTransaction == null && Database.IsRelational())
    {
      return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
      {
        var (standaloneRun, standalonePending, trackedSnapshots) = CaptureStandaloneSaveState();
        await using var tx = await Database.BeginTransactionAsync(cancellationToken);
        return await SaveStandaloneWithJournalAsync(
          standaloneRun, standalonePending, trackedSnapshots, acceptAllChangesOnSuccess, tx, cancellationToken);
      });
    }

    var (run, pendingStates, snapshots) = CaptureStandaloneSaveState();
    return await SaveStandaloneWithJournalAsync(
      run, pendingStates, snapshots, acceptAllChangesOnSuccess, tx: null, cancellationToken);
  }

  private (MutationRunScope Run, List<MutationRunEntryState> Pending, List<StandaloneSaveEntrySnapshot> Snapshots)
    CaptureStandaloneSaveState()
  {
    var snapshots = StandaloneSaveEntrySnapshot.Capture(ChangeTracker);
    var run = new MutationRunScope(static () => { });
    var pending = ChangeJournalRecorder.CaptureRunFlushStates(
      ChangeTracker, TenantContext, run, _suppressAllAudit, _suppressDerivedAudit);
    return (run, pending, snapshots);
  }

  public async Task FlushMutationJournalAsync(CancellationToken cancellationToken = default)
  {
    if (_activeMutationRun == null || _activeMutationRun.Depth > 1)
    {
      return;
    }

    if (ChangeTracker.HasChanges())
    {
      await SaveChangesAsync(cancellationToken);
    }

    var rows = ChangeJournalRecorder.BuildConsolidatedJournalRows(_activeMutationRun, TenantContext, Time);
    _activeMutationRun.ClearJournal();
    if (rows.Count == 0)
    {
      return;
    }

    await PersistJournalRowsAsync(acceptAllChangesOnSuccess: true, rows, cancellationToken);
  }

  public IDisposable SuppressAuditScope()
  {
    var prev = _suppressAllAudit;
    _suppressAllAudit = true;
    return new DisposableScope(() => _suppressAllAudit = prev);
  }

  public int? GetInitialMutationVersion(Type entityType, long id) =>
    _activeMutationRun != null && _activeMutationRun.InitialVersions.TryGetValue((entityType, id), out var ver)
      ? ver
      : null;

  public async Task RunDerivedRecalculationAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
  {
    if (ChangeTracker.HasChanges())
    {
      await SaveChangesAsync(cancellationToken);
    }

    var prev = _suppressDerivedAudit;
    _suppressDerivedAudit = true;
    try
    {
      await action(cancellationToken);
      if (ChangeTracker.HasChanges())
      {
        await SaveChangesAsync(cancellationToken);
      }
    }
    finally
    {
      _suppressDerivedAudit = prev;
    }
  }

  private async Task<int> SaveStandaloneWithJournalAsync(
    MutationRunScope run,
    List<MutationRunEntryState> pending,
    List<StandaloneSaveEntrySnapshot> trackedSnapshots,
    bool acceptAllChangesOnSuccess,
    IDbContextTransaction? tx,
    CancellationToken cancellationToken)
  {
    try
    {
      var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken);
      ChangeTracker.AcceptAllChanges();
      ChangeJournalRecorder.CompleteRunFlushStates(pending);
      var journalRows = ChangeJournalRecorder.BuildConsolidatedJournalRows(run, TenantContext, Time);
      if (journalRows.Count > 0)
      {
        await PersistJournalRowsAsync(acceptAllChangesOnSuccess: false, journalRows, cancellationToken);
      }

      if (tx != null)
      {
        await tx.CommitAsync(cancellationToken);
      }

      if (!acceptAllChangesOnSuccess)
      {
        StandaloneSaveEntrySnapshot.Restore(trackedSnapshots, resetGeneratedKeys: false);
      }

      return result;
    }
    catch
    {
      StandaloneSaveEntrySnapshot.Restore(trackedSnapshots, resetGeneratedKeys: tx != null);
      throw;
    }
  }

  private async Task PersistJournalRowsAsync(
    bool acceptAllChangesOnSuccess,
    List<AppPlatformChange> journalRows,
    CancellationToken cancellationToken)
  {
    try
    {
      AppPlatformChange.AddRange(journalRows);
      await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    finally
    {
      foreach (var r in journalRows)
      {
        Entry(r).State = EntityState.Detached;
      }
    }
  }
}
