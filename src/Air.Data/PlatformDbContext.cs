using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
  public DbSet<Entities.AppPlatformChange> AppPlatformChange => Set<Entities.AppPlatformChange>();

  public long RequiredTenantId => TenantContext.TenantId
    ?? throw new InvalidOperationException("Operation requires an active tenant, but none was resolved.");

  private MutationRunScope? _activeMutationRun;

  public bool IsInMutationRun => _activeMutationRun != null;

  public bool IsMutationRollbackOnly => _activeMutationRun?.IsRollbackOnly == true;

  public void MarkMutationRollbackOnly()
  {
    _activeMutationRun?.IsRollbackOnly = true;
  }

  internal Guid? CurrentMutationRunId => _activeMutationRun?.RunId;

  public IDisposable BeginMutationRun()
  {
    if (_activeMutationRun != null)
    {
      var parentRun = _activeMutationRun;
      parentRun.Depth++;
      var nestedDisposed = 0;
      return new NestedMutationScope(() =>
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

  private static readonly ConcurrentDictionary<(Type ContextType, Type BaseType), Func<PlatformDbContext, object?>?> BaseSetAccessors = new();

  public IQueryable<TBase>? SetOfBase<TBase>() where TBase : class
  {
    var accessor = BaseSetAccessors.GetOrAdd((GetType(), typeof(TBase)), key =>
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

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    {
      var clrType = entityType.ClrType;

      if (typeof(IHasTenant).IsAssignableFrom(clrType))
      {
        var parameter = Expression.Parameter(clrType, "e");
        var property = Expression.Call(
          typeof(EF),
          nameof(EF.Property),
          [typeof(long)],
          parameter,
          Expression.Constant(nameof(IHasTenant.TenantId)));

        var tenantIdExpr = Expression.Property(Expression.Constant(this), nameof(TenantContext));
        var isSystemScopeExpr = Expression.Property(tenantIdExpr, nameof(ITenantContext.IsSystemScope));
        var currentTenantId = Expression.Property(tenantIdExpr, nameof(ITenantContext.TenantId));

        // Filter: TenantContext.IsSystemScope || EF.Property<long>(e, "TenantId") == TenantContext.TenantId
        var convertedProperty = Expression.Convert(property, typeof(long?));
        var equality = Expression.Equal(convertedProperty, currentTenantId);
        var filterBody = Expression.OrElse(isSystemScopeExpr, equality);

        var lambda = Expression.Lambda(filterBody, parameter);
        modelBuilder.Entity(clrType).HasQueryFilter(TenantFilterExtensions.Name, lambda);
      }

      if (typeof(IInheritedByChildTenants).IsAssignableFrom(clrType))
      {
        var parameter = Expression.Parameter(clrType, "e");
        var property = Expression.Call(
          typeof(EF),
          nameof(EF.Property),
          [typeof(long?)],
          parameter,
          Expression.Constant(nameof(IInheritedByChildTenants.TenantId)));

        var tenantIdExpr = Expression.Property(Expression.Constant(this), nameof(TenantContext));
        var isSystemScopeExpr = Expression.Property(tenantIdExpr, nameof(ITenantContext.IsSystemScope));
        var currentTenantId = Expression.Property(tenantIdExpr, nameof(ITenantContext.TenantId));

        // Filter: TenantContext.IsSystemScope || EF.Property<long?>(e, "TenantId") == null || EF.Property<long?>(e, "TenantId") == TenantContext.TenantId
        var nullTenantCheck = Expression.Equal(property, Expression.Constant(null, typeof(long?)));
        var equality = Expression.Equal(property, currentTenantId);
        var filterBody = Expression.OrElse(isSystemScopeExpr, Expression.OrElse(nullTenantCheck, equality));

        var lambda = Expression.Lambda(filterBody, parameter);
        modelBuilder.Entity(clrType).HasQueryFilter(TenantFilterExtensions.Inherited, lambda);
        modelBuilder.Entity(clrType).Property(nameof(IInheritedByChildTenants.TenantId)).IsConcurrencyToken();
      }

      if (typeof(IHasVersion).IsAssignableFrom(clrType))
      {
        modelBuilder.Entity(clrType).Property(nameof(IHasVersion.Ver)).IsConcurrencyToken();
      }
    }
  }

  protected sealed override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
    PlatformMigrationHistory.Apply(optionsBuilder, ModuleHandle, GetType());

  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
    base.ConfigureConventions(configurationBuilder);
    configurationBuilder.Conventions.Add(_ => new ModuleOwnershipConvention(GetType()));
    configurationBuilder.Properties<DateTime>().HaveConversion<LocalDateTimeConverter>();
    configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcOffsetConverter>();
  }

  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
    ChangeTracker.DetectChanges();
    CascadeLoader.LoadDependents(this);

    ApplyEntityConventions();

    if (_activeMutationRun != null)
    {
      var pending = CaptureRunFlushStates(_activeMutationRun);
      var res = base.SaveChanges(acceptAllChangesOnSuccess);
      CompleteRunFlushStates(pending);
      return res;
    }

    var standaloneRun = new MutationRunScope(static () => { });
    var standalonePending = CaptureRunFlushStates(standaloneRun);
    if (standaloneRun.JournalEntries.Count == 0)
    {
      return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    if (Database.CurrentTransaction == null && Database.IsRelational())
    {
      return Database.CreateExecutionStrategy().Execute(() =>
      {
        using var tx = Database.BeginTransaction();
        var res = SaveStandaloneWithJournal(acceptAllChangesOnSuccess, standaloneRun, standalonePending);
        tx.Commit();
        return res;
      });
    }

    return SaveStandaloneWithJournal(acceptAllChangesOnSuccess, standaloneRun, standalonePending);
  }

  public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ChangeTracker.DetectChanges();
    await CascadeLoader.LoadDependentsAsync(this, cancellationToken);

    ApplyEntityConventions();

    if (_activeMutationRun != null)
    {
      var pending = CaptureRunFlushStates(_activeMutationRun);
      var res = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
      CompleteRunFlushStates(pending);
      return res;
    }

    var standaloneRun = new MutationRunScope(static () => { });
    var standalonePending = CaptureRunFlushStates(standaloneRun);
    if (standaloneRun.JournalEntries.Count == 0)
    {
      return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    if (Database.CurrentTransaction == null && Database.IsRelational())
    {
      return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
      {
        await using var tx = await Database.BeginTransactionAsync(cancellationToken);
        var res = await SaveStandaloneWithJournalAsync(acceptAllChangesOnSuccess, standaloneRun, standalonePending, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return res;
      });
    }

    return await SaveStandaloneWithJournalAsync(acceptAllChangesOnSuccess, standaloneRun, standalonePending, cancellationToken);
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

    var rows = BuildConsolidatedJournalRows(_activeMutationRun);
    _activeMutationRun.JournalEntries.Clear();
    if (rows.Count == 0)
    {
      return;
    }

    await PersistJournalRowsAsync(true, rows, cancellationToken);
  }

  private int SaveStandaloneWithJournal(
    bool acceptAllChangesOnSuccess,
    MutationRunScope run,
    List<MutationRunEntryState> pending)
  {
    var result = base.SaveChanges(acceptAllChangesOnSuccess);
    CompleteRunFlushStates(pending);
    var journalRows = BuildConsolidatedJournalRows(run);
    if (journalRows.Count > 0)
    {
      PersistJournalRows(acceptAllChangesOnSuccess, journalRows);
    }

    return result;
  }

  private async Task<int> SaveStandaloneWithJournalAsync(
    bool acceptAllChangesOnSuccess,
    MutationRunScope run,
    List<MutationRunEntryState> pending,
    CancellationToken cancellationToken)
  {
    var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    CompleteRunFlushStates(pending);
    var journalRows = BuildConsolidatedJournalRows(run);
    if (journalRows.Count > 0)
    {
      await PersistJournalRowsAsync(acceptAllChangesOnSuccess, journalRows, cancellationToken);
    }

    return result;
  }

  private void PersistJournalRows(bool acceptAllChangesOnSuccess, List<Entities.AppPlatformChange> journalRows)
  {
    try
    {
      AppPlatformChange.AddRange(journalRows);
      base.SaveChanges(acceptAllChangesOnSuccess);
    }
    finally
    {
      DetachJournalRows(journalRows);
    }
  }

  private async Task PersistJournalRowsAsync(
    bool acceptAllChangesOnSuccess,
    List<Entities.AppPlatformChange> journalRows,
    CancellationToken cancellationToken)
  {
    try
    {
      AppPlatformChange.AddRange(journalRows);
      await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    finally
    {
      DetachJournalRows(journalRows);
    }
  }

  private List<MutationRunEntryState> CaptureRunFlushStates(MutationRunScope run)
  {
    var pendingEntries = ChangeTracker.Entries()
      .Where(e => IsJournaled(e) && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
      .ToList();

    if (pendingEntries.Count == 0)
    {
      return [];
    }

    var tenantId = TenantContext.TenantId ?? 0;
    var toCompleteAfterFlush = new List<MutationRunEntryState>(pendingEntries.Count);

    foreach (var entry in pendingEntries)
    {
      var table = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
      var rowTenantId = entry.Entity is IHasTenant t && t.TenantId != 0
        ? t.TenantId
        : entry.Entity is IInheritedByChildTenants it && it.TenantId is > 0
          ? it.TenantId.Value
          : tenantId;
      var excluded = EntityAuditMetadata.For(entry.Metadata.ClrType).ExcludedProperties;

      run.JournalEntries.TryGetValue(entry.Entity, out var state);

      if (entry.State == EntityState.Added)
      {
        state = new MutationRunEntryState
        {
          Entry = entry,
          ClrType = entry.Metadata.ClrType,
          Table = table,
          TenantId = rowTenantId,
          IsAdded = true,
          Operation = Entities.ChangeOperations.Added
        };
        run.JournalEntries[entry.Entity] = state;
        toCompleteAfterFlush.Add(state);
      }
      else if (entry.State == EntityState.Modified)
      {
        if (state is { IsAdded: true })
        {
          toCompleteAfterFlush.Add(state);
          continue;
        }

        if (state == null)
        {
          state = new MutationRunEntryState
          {
            Entry = entry,
            ClrType = entry.Metadata.ClrType,
            Table = table,
            TenantId = rowTenantId,
            RowId = ((IEntity)entry.Entity).Id,
            Operation = Entities.ChangeOperations.Modified
          };
          run.JournalEntries[entry.Entity] = state;
        }

        foreach (var p in entry.Properties)
        {
          if (!p.IsModified || Equals(p.OriginalValue, p.CurrentValue) || excluded.Contains(p.Metadata.Name))
          {
            continue;
          }

          if (!state.FirstBefore.ContainsKey(p.Metadata.Name))
          {
            state.FirstBefore[p.Metadata.Name] = p.OriginalValue;
          }

          state.LatestAfter[p.Metadata.Name] = p.CurrentValue;
          if (p.Metadata.Name != nameof(IHasVersion.Ver))
          {
            state.TouchedProperties.Add(p.Metadata.Name);
          }
        }
      }
      else if (entry.State == EntityState.Deleted)
      {
        if (state is { IsAdded: true })
        {
          state.IsDeleted = true;
          run.JournalEntries.Remove(entry.Entity);
          continue;
        }

        if (state == null)
        {
          state = new MutationRunEntryState
          {
            Entry = entry,
            ClrType = entry.Metadata.ClrType,
            Table = table,
            TenantId = rowTenantId,
            RowId = ((IEntity)entry.Entity).Id,
            Operation = Entities.ChangeOperations.Deleted,
            IsDeleted = true
          };
          run.JournalEntries[entry.Entity] = state;
        }
        else
        {
          state.Operation = Entities.ChangeOperations.Deleted;
          state.IsDeleted = true;
        }

        foreach (var p in entry.Properties)
        {
          if (excluded.Contains(p.Metadata.Name))
          {
            continue;
          }

          if (!state.FirstBefore.ContainsKey(p.Metadata.Name))
          {
            state.FirstBefore[p.Metadata.Name] = p.OriginalValue;
          }
        }

        state.LatestAfter.Clear();
      }
    }

    return toCompleteAfterFlush;
  }

  private static void CompleteRunFlushStates(List<MutationRunEntryState> states)
  {
    foreach (var state in states)
    {
      if (state.IsDeleted)
      {
        continue;
      }

      if (state.IsAdded)
      {
        state.RowId = ((IEntity)state.Entry.Entity).Id;
        var excluded = EntityAuditMetadata.For(state.ClrType).ExcludedProperties;
        state.LatestAfter.Clear();
        foreach (var p in state.Entry.Properties)
        {
          if (!excluded.Contains(p.Metadata.Name))
          {
            state.LatestAfter[p.Metadata.Name] = p.CurrentValue;
          }
        }
      }
    }
  }

  private List<Entities.AppPlatformChange> BuildConsolidatedJournalRows(MutationRunScope run)
  {
    if (run.JournalEntries.Count == 0)
    {
      return [];
    }

    var now = DateTimeOffset.UtcNow;
    var userId = TenantContext.UserId;
    var correlationId = TenantContext.CorrelationId;
    var rows = new List<Entities.AppPlatformChange>(run.JournalEntries.Count);

    foreach (var state in run.JournalEntries.Values)
    {
      var row = new Entities.AppPlatformChange
      {
        RunId = run.RunId,
        At = now,
        UserId = userId,
        CorrelationId = correlationId,
        TenantId = state.TenantId,
        Table = state.Table,
        RowId = state.RowId,
        Operation = state.Operation
      };

      if (state.IsAdded)
      {
        row.After = System.Text.Json.JsonSerializer.Serialize(state.LatestAfter, AuditJsonOptions);
        rows.Add(row);
      }
      else if (state.Operation == Entities.ChangeOperations.Deleted)
      {
        row.Before = System.Text.Json.JsonSerializer.Serialize(state.FirstBefore, AuditJsonOptions);
        rows.Add(row);
      }
      else
      {
        var changedProps = state.TouchedProperties
          .Where(prop => !Equals(state.FirstBefore.GetValueOrDefault(prop), state.LatestAfter.GetValueOrDefault(prop)))
          .ToList();
        if (!changedProps.Any(prop => prop is not (nameof(IHasTimestamps.UpdatedAt) or nameof(IHasTimestamps.CreatedAt) or nameof(IHasVersion.Ver))))
        {
          continue;
        }

        var beforeDict = new Dictionary<string, object?>(StringComparer.Ordinal);
        var afterDict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in changedProps)
        {
          beforeDict[prop] = state.FirstBefore.GetValueOrDefault(prop);
          afterDict[prop] = state.LatestAfter.GetValueOrDefault(prop);
        }

        row.Before = System.Text.Json.JsonSerializer.Serialize(beforeDict, AuditJsonOptions);
        row.After = System.Text.Json.JsonSerializer.Serialize(afterDict, AuditJsonOptions);
        rows.Add(row);
      }
    }

    return rows;
  }

  private void DetachJournalRows(List<Entities.AppPlatformChange> rows)
  {
    foreach (var r in rows)
    {
      Entry(r).State = EntityState.Detached;
    }
  }

  private bool _suppressDerivedAudit;

  public async Task RunDerivedRecalculationAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
  {
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

  private bool IsJournaled(EntityEntry entry)
  {
    if (_suppressDerivedAudit && entry.State == EntityState.Modified)
    {
      return false;
    }

    var auditMeta = EntityAuditMetadata.For(entry.Metadata.ClrType);
    if (!auditMeta.IsEntityAudited)
    {
      return false;
    }

    return entry.State != EntityState.Modified ||
           entry.Properties.Any(p => p.IsModified &&
                                     !Equals(p.OriginalValue, p.CurrentValue) &&
                                     p.Metadata.Name != nameof(IHasVersion.Ver) &&
                                     !auditMeta.ExcludedProperties.Contains(p.Metadata.Name));
  }

  private bool ChangesNoAuditOnly(EntityEntry entry)
  {
    if (entry.State != EntityState.Modified)
    {
      return false;
    }

    if (_suppressDerivedAudit)
    {
      return true;
    }

    var noAuditProps = EntityAuditMetadata.For(entry.Metadata.ClrType).NoAuditProperties;
    if (noAuditProps.Count == 0)
    {
      return false;
    }

    var hasModified = false;
    foreach (var p in entry.Properties)
    {
      if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
      {
        hasModified = true;
        if (!noAuditProps.Contains(p.Metadata.Name))
        {
          return false;
        }
      }
    }

    return hasModified;
  }

  private static readonly System.Text.Json.JsonSerializerOptions AuditJsonOptions = new()
  {
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  private void ApplyEntityConventions()
  {
    var now = DateTimeOffset.UtcNow;
    var tenantId = TenantContext.TenantId;

    foreach (var entry in ChangeTracker.Entries())
    {
      if (entry.Entity is Entities.AppPlatformChange && entry.State is EntityState.Modified or EntityState.Deleted)
      {
        throw new InvalidOperationException("AppPlatformChange rows are immutable and cannot be modified or deleted.");
      }

      if (entry.State is EntityState.Modified or EntityState.Deleted)
      {
        if (entry.Entity is IHasTenant existingTenant && tenantId.HasValue)
        {
          var origTid = entry.Property(nameof(IHasTenant.TenantId)).OriginalValue as long? ?? existingTenant.TenantId;
          if (origTid != 0 && origTid != tenantId.Value || existingTenant.TenantId != 0 && existingTenant.TenantId != tenantId.Value)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete {entry.Metadata.ClrType.Name} belonging to another tenant."
            );
          }
        }

        if (entry.Entity is IInheritedByChildTenants existingInherited && tenantId.HasValue && !TenantContext.IsSystemScope)
        {
          var origInheritedTid = entry.Property(nameof(IInheritedByChildTenants.TenantId)).OriginalValue as long?;
          if (origInheritedTid is { } ownerTid && ownerTid != tenantId.Value ||
              entry.State == EntityState.Modified && existingInherited.TenantId is { } newTid && newTid != 0 && newTid != tenantId.Value)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete inherited {entry.Metadata.ClrType.Name} belonging to another tenant."
            );
          }
        }
      }

      if (entry.State == EntityState.Added)
      {
        if (entry.Entity is IHasTimestamps timestamps)
        {
          timestamps.CreatedAt = now;
        }

        if (entry.Entity is IHasTenant tenantEntity)
        {
          if (tenantEntity.TenantId == 0)
          {
            tenantEntity.TenantId = tenantId ?? throw new InvalidOperationException(
              $"Cannot insert {entry.Metadata.ClrType.Name} without an active tenant."
            );
          }
          else if (tenantId.HasValue && tenantEntity.TenantId != tenantId.Value)
          {
            throw new InvalidOperationException(
              $"Cannot insert {entry.Metadata.ClrType.Name} belonging to another tenant."
            );
          }
        }

        if (entry.Entity is IInheritedByChildTenants inherited)
        {
          if (tenantId.HasValue && !TenantContext.IsSystemScope)
          {
            if (inherited.TenantId is null or 0)
            {
              inherited.TenantId = tenantId.Value;
            }
            else if (inherited.TenantId != tenantId.Value)
            {
              throw new InvalidOperationException(
                $"Cannot insert inherited {entry.Metadata.ClrType.Name} belonging to another tenant."
              );
            }
          }
          else if (inherited.TenantId == 0)
          {
            inherited.TenantId = tenantId;
          }
        }

        if (entry.Entity is IHasVersion version)
        {
          if (version.Ver == 0)
          {
            version.Ver = 1;
          }

          _activeMutationRun?.VersionIncrementedEntities.Add(entry.Entity);
        }
      }
      else if (entry.State == EntityState.Modified)
      {
        var noAuditOnly = ChangesNoAuditOnly(entry);

        if (entry.Entity is IHasTimestamps timestamps)
        {
          entry.Property(nameof(IHasTimestamps.CreatedAt)).IsModified = false;
          if (!noAuditOnly)
          {
            timestamps.UpdatedAt = now;
          }
        }

        if (entry.Entity is IHasTenant)
        {
          var tenantProp = entry.Property(nameof(IHasTenant.TenantId));
          if (tenantProp.OriginalValue is long origTid && origTid > 0)
          {
            tenantProp.CurrentValue = origTid;
          }

          tenantProp.IsModified = false;
        }

        if (entry.Entity is IInheritedByChildTenants && !TenantContext.IsSystemScope)
        {
          var tenantProp = entry.Property(nameof(IInheritedByChildTenants.TenantId));
          tenantProp.CurrentValue = tenantProp.OriginalValue;
          tenantProp.IsModified = false;
        }

        if (entry.Entity is IHasVersion version)
        {
          var verProp = entry.Property(nameof(IHasVersion.Ver));
          if (noAuditOnly || _activeMutationRun != null && !_activeMutationRun.VersionIncrementedEntities.Add(entry.Entity))
          {
            if (verProp.OriginalValue is int origV)
            {
              version.Ver = origV;
            }

            verProp.IsModified = false;
          }
          else
          {
            var origVer = verProp.OriginalValue as int? ?? version.Ver;
            version.Ver = origVer + 1;
          }
        }
      }
    }
  }

  private sealed class NestedMutationScope(Action onDispose) : IDisposable
  {
    public void Dispose() => onDispose();
  }

  private sealed class LocalDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
    v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
    v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));

  private sealed class UtcOffsetConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(
    v => v.ToUniversalTime(),
    v => v.ToUniversalTime());
}
