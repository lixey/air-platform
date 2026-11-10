using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Data.Internals;

namespace Platform.Data;

/// <summary>
/// Base EF Core DbContext for the platform and its modules.
/// Automates tenant isolation (IHasTenant), audit timestamps (IHasTimestamps),
/// and optimistic concurrency versioning (IHasVersion).
/// </summary>
public abstract class PlatformDbContext : DbContext
{
  protected PlatformDbContext()
  {
  }

  protected PlatformDbContext(DbContextOptions options) : base(options)
  {
  }

  protected PlatformDbContext(DbContextOptions options, ITenantContext? tenantContext) : base(options)
  {
    TenantContext = tenantContext;
  }

  [AllowNull]
  public ITenantContext TenantContext => field ?? AsyncLocalTenantContext.Instance;

  public string? ModuleHandle => ModuleOwnershipExtensions.GetDeclaredModuleHandle(GetType());

  public DbSet<AppLanguage> AppLanguage => Set<AppLanguage>();
  public DbSet<AppAgentProvider> AppAgentProvider => Set<AppAgentProvider>();
  public DbSet<AppAgentModel> AppAgentModel => Set<AppAgentModel>();
  public DbSet<AppAgentMemory> AppAgentMemory => Set<AppAgentMemory>();
  public DbSet<AppPlatformInstallation> AppPlatformInstallation => Set<AppPlatformInstallation>();
  public DbSet<AppPlatformChange> AppPlatformChange => Set<AppPlatformChange>();

  public long RequiredTenantId => TenantContext.TenantId
                                  ?? throw new InvalidOperationException("Operation requires an active tenant, but none was resolved.");

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

  private static readonly ConcurrentDictionary<(Type ContextType, Type BaseType), Func<PlatformDbContext, object?>?> BaseSetAccessors =
    new();

  public IQueryable<TBase>? SetOfBase<TBase>() where TBase : class
  {
    var accessor = BaseSetAccessors.GetOrAdd(
      (GetType(), typeof(TBase)),
      key =>
      {
        var clrType = Model.GetEntityTypes()
          .Select(t => t.ClrType)
          .FirstOrDefault(t => !t.IsAbstract && key.BaseType.IsAssignableFrom(t));
        if (clrType is null)
        {
          return null;
        }

        var ctxParam = Expression.Parameter(typeof(PlatformDbContext), "ctx");
        var call = Expression.Call(ctxParam, nameof(Set), [clrType]);
        return Expression.Lambda<Func<PlatformDbContext, object?>>(call, ctxParam).Compile();
      });
    return (IQueryable<TBase>?)accessor?.Invoke(this);
  }

  public async Task<Dictionary<long, string>> LoadUserNamesAsync(
    IReadOnlyCollection<long> userIds,
    CancellationToken cancellationToken = default)
  {
    var result = new Dictionary<long, string>();
    if (userIds.Count == 0 || SetOfBase<BaseAppUser>() is not { } userQuery)
    {
      return result;
    }

    try
    {
      var rows = await userQuery
        .AsNoTracking()
        .Where(u => userIds.Contains(u.Id))
        .Select(u => new { u.Id, u.Name, u.Login })
        .ToListAsync(cancellationToken);

      foreach (var r in rows)
      {
        result[r.Id] = !string.IsNullOrWhiteSpace(r.Name) ? r.Name : r.Login;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
    }

    return result;
  }

  public void ResetMutationState()
  {
    _activeMutationRun = null;
    ChangeTracker.Clear();
  }

  protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
    PlatformMigrationHistory.Apply(optionsBuilder, ModuleHandle, GetType());

  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
    base.ConfigureConventions(configurationBuilder);
    configurationBuilder.Conventions.Add(_ => new PlatformModelConvention(this));
    configurationBuilder.Conventions.Add(_ => new ModuleOwnershipConvention(GetType()));
    configurationBuilder.Properties<DateTime>().HaveConversion<LocalDateTimeConverter>();
    configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcOffsetConverter>();
  }

  public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
    throw new NotSupportedException("Synchronous SaveChanges is disabled in PlatformDbContext. Use SaveChangesAsync instead.");

  public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ChangeTracker.DetectChanges();
    await CascadeLoader.LoadDependentsAsync(this, cancellationToken);

    EntityWriteConventions.Apply(ChangeTracker, TenantContext, _activeMutationRun, _suppressDerivedAudit);

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
        var trackedSnapshots = StandaloneSaveEntrySnapshot.Capture(ChangeTracker);
        var standaloneRun = new MutationRunScope(static () => { });
        var standalonePending = ChangeJournalRecorder.CaptureRunFlushStates(
          ChangeTracker, TenantContext, standaloneRun, _suppressAllAudit, _suppressDerivedAudit);
        await using var tx = await Database.BeginTransactionAsync(cancellationToken);
        return await SaveStandaloneWithJournalAsync(
          standaloneRun, standalonePending, trackedSnapshots, acceptAllChangesOnSuccess, tx, cancellationToken);
      });
    }

    {
      var trackedSnapshots = StandaloneSaveEntrySnapshot.Capture(ChangeTracker);
      var standaloneRun = new MutationRunScope(static () => { });
      var standalonePending = ChangeJournalRecorder.CaptureRunFlushStates(
        ChangeTracker, TenantContext, standaloneRun, _suppressAllAudit, _suppressDerivedAudit);
      return await SaveStandaloneWithJournalAsync(
        standaloneRun, standalonePending, trackedSnapshots, acceptAllChangesOnSuccess, tx: null, cancellationToken);
    }
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

    var rows = ChangeJournalRecorder.BuildConsolidatedJournalRows(_activeMutationRun, TenantContext);
    _activeMutationRun.JournalEntries.Clear();
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
      var journalRows = ChangeJournalRecorder.BuildConsolidatedJournalRows(run, TenantContext);
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
