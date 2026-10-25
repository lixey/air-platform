using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
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
        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
        modelBuilder.Entity(clrType).Property(nameof(IInheritedByChildTenants.TenantId)).IsConcurrencyToken();
      }

      if (typeof(IHasVersion).IsAssignableFrom(clrType))
      {
        modelBuilder.Entity(clrType).Property(nameof(IHasVersion.Ver)).IsConcurrencyToken();
      }

      foreach (var prop in entityType.GetProperties())
      {
        if (((MemberInfo?)prop.PropertyInfo ?? prop.FieldInfo) is { } member &&
            Attribute.IsDefined(member, typeof(KeepsVersionAttribute), inherit: true))
        {
          prop.IsConcurrencyToken = true;
        }
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
    ApplyEntityConventions();

    var journalSnapshots = CaptureJournalSnapshots();
    if (journalSnapshots.Count == 0)
    {
      return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    if (Database.CurrentTransaction == null && Database.IsRelational())
    {
      return Database.CreateExecutionStrategy().Execute(() =>
      {
        using var tx = Database.BeginTransaction();
        var res = SaveWithJournalCore(acceptAllChangesOnSuccess, journalSnapshots);
        tx.Commit();
        return res;
      });
    }

    return SaveWithJournalCore(acceptAllChangesOnSuccess, journalSnapshots);
  }

  public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ChangeTracker.DetectChanges();
    ApplyEntityConventions();

    var journalSnapshots = CaptureJournalSnapshots();
    if (journalSnapshots.Count == 0)
    {
      return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    if (Database.CurrentTransaction == null && Database.IsRelational())
    {
      return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
      {
        await using var tx = await Database.BeginTransactionAsync(cancellationToken);
        var res = await SaveWithJournalCoreAsync(acceptAllChangesOnSuccess, journalSnapshots, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return res;
      });
    }

    return await SaveWithJournalCoreAsync(acceptAllChangesOnSuccess, journalSnapshots, cancellationToken);
  }

  private int SaveWithJournalCore(
    bool acceptAllChangesOnSuccess,
    List<(EntityEntry Entry, Entities.AppPlatformChange Row)> journalSnapshots)
  {
    var result = base.SaveChanges(acceptAllChangesOnSuccess);
    var journalRows = CompleteJournalRows(journalSnapshots);
    if (journalRows.Count == 0)
    {
      return result;
    }

    var activeTx = Database.CurrentTransaction;
    try
    {
      activeTx?.CreateSavepoint("air_journal");
      AppPlatformChange.AddRange(journalRows);
      base.SaveChanges(acceptAllChangesOnSuccess);
    }
    catch (Exception ex) when (IsMissingJournalTable(ex))
    {
      activeTx?.RollbackToSavepoint("air_journal");
    }
    finally
    {
      DetachJournalRows(journalRows);
    }

    return result;
  }

  private async Task<int> SaveWithJournalCoreAsync(
    bool acceptAllChangesOnSuccess,
    List<(EntityEntry Entry, Entities.AppPlatformChange Row)> journalSnapshots,
    CancellationToken cancellationToken)
  {
    var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    var journalRows = CompleteJournalRows(journalSnapshots);
    if (journalRows.Count == 0)
    {
      return result;
    }

    var activeTx = Database.CurrentTransaction;
    try
    {
      if (activeTx != null)
      {
        await activeTx.CreateSavepointAsync("air_journal", cancellationToken);
      }

      AppPlatformChange.AddRange(journalRows);
      await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    catch (Exception ex) when (IsMissingJournalTable(ex))
    {
      if (activeTx != null)
      {
        await activeTx.RollbackToSavepointAsync("air_journal", cancellationToken);
      }
    }
    finally
    {
      DetachJournalRows(journalRows);
    }

    return result;
  }

  private static bool IsMissingJournalTable(Exception ex)
  {
    var msg = ex.InnerException?.Message ?? ex.Message;
    return msg.Contains(nameof(AppPlatformChange), StringComparison.OrdinalIgnoreCase) &&
           (msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("no such table", StringComparison.OrdinalIgnoreCase));
  }

  private readonly HashSet<object> _restoredEntities = new(ReferenceEqualityComparer.Instance);

  public void MarkRestored(object entity)
  {
    ArgumentNullException.ThrowIfNull(entity);
    _restoredEntities.Add(entity);
  }

  private List<(EntityEntry Entry, Entities.AppPlatformChange Row)> CaptureJournalSnapshots()
  {
    var pendingEntries = ChangeTracker.Entries()
      .Where(e => IsJournaled(e) && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
      .ToList();

    if (pendingEntries.Count == 0)
    {
      _restoredEntities.Clear();
      return [];
    }

    var now = DateTimeOffset.UtcNow;
    var runId = Guid.CreateVersion7();
    var tenantId = TenantContext.TenantId ?? 0;
    var userId = TenantContext.UserId;
    var correlationId = TenantContext.CorrelationId;

    var journalSnapshots = new List<(EntityEntry Entry, Entities.AppPlatformChange Row)>(pendingEntries.Count);
    foreach (var entry in pendingEntries)
    {
      var isRestored = _restoredEntities.Remove(entry.Entity);
      var table = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
      var row = new Entities.AppPlatformChange
      {
        RunId = runId,
        At = now,
        UserId = userId,
        CorrelationId = correlationId,
        TenantId = entry.Entity is IHasTenant t && t.TenantId != 0 ? t.TenantId : tenantId,
        Table = table
      };

      if (entry.State == EntityState.Added)
      {
        row.Operation = isRestored ? Entities.ChangeOperations.Restored : Entities.ChangeOperations.Added;
      }
      else if (entry.State == EntityState.Modified)
      {
        var auditMeta = EntityAuditMetadata.For(entry.Metadata.ClrType);
        var modified = entry.Properties
          .Where(p => p.IsModified &&
                      !Equals(p.OriginalValue, p.CurrentValue) &&
                      p.Metadata.Name != nameof(IHasVersion.Ver) &&
                      !auditMeta.ExcludedProperties.Contains(p.Metadata.Name))
          .ToList();
        if (modified.Count == 0)
        {
          continue;
        }

        row.Operation = isRestored ? Entities.ChangeOperations.Restored : Entities.ChangeOperations.Modified;
        row.RowId = ((IEntity)entry.Entity).Id;
        row.Before = SerializeProperties(entry.Metadata.ClrType, modified, p => p.OriginalValue);
        row.After = SerializeProperties(entry.Metadata.ClrType, modified, p => p.CurrentValue);
      }
      else if (entry.State == EntityState.Deleted)
      {
        row.Operation = Entities.ChangeOperations.Deleted;
        row.RowId = ((IEntity)entry.Entity).Id;
        row.Before = SerializeProperties(entry.Metadata.ClrType, entry.Properties, p => p.OriginalValue);
      }

      journalSnapshots.Add((entry, row));
    }

    _restoredEntities.Clear();
    return journalSnapshots;
  }

  private static List<Entities.AppPlatformChange> CompleteJournalRows(List<(EntityEntry Entry, Entities.AppPlatformChange Row)> snapshots)
  {
    var rows = new List<Entities.AppPlatformChange>(snapshots.Count);
    foreach (var (entry, row) in snapshots)
    {
      if (row.Operation is Entities.ChangeOperations.Added or Entities.ChangeOperations.Restored && row.RowId == 0)
      {
        row.RowId = ((IEntity)entry.Entity).Id;
        row.After = SerializeProperties(entry.Metadata.ClrType, entry.Properties, p => p.CurrentValue);
      }

      rows.Add(row);
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

  private static bool IsJournaled(EntityEntry entry)
  {
    var auditMeta = EntityAuditMetadata.For(entry.Metadata.ClrType);
    if (!auditMeta.IsEntityAudited || ChangesLoginCountersOnly(entry))
    {
      return false;
    }

    return entry.State != EntityState.Modified ||
           entry.Properties.Any(p => p.IsModified &&
                                     !Equals(p.OriginalValue, p.CurrentValue) &&
                                     p.Metadata.Name != nameof(IHasVersion.Ver) &&
                                     !auditMeta.ExcludedProperties.Contains(p.Metadata.Name));
  }

  private static bool ChangesLoginCountersOnly(EntityEntry entry) =>
    entry.Entity is BaseAppUser &&
    entry.State == EntityState.Modified &&
    entry.Properties.Any(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)) &&
    entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).All(p => BaseAppUser.LoginCounters.Contains(p.Metadata.Name));

  private static bool ChangesKeepsVersionOnly(EntityEntry entry)
  {
    if (entry.State != EntityState.Modified)
    {
      return false;
    }

    var keepsProps = EntityAuditMetadata.For(entry.Metadata.ClrType).KeepsVersionProperties;
    if (keepsProps.Count == 0)
    {
      return false;
    }

    var hasModified = false;
    foreach (var p in entry.Properties)
    {
      if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
      {
        hasModified = true;
        if (!keepsProps.Contains(p.Metadata.Name))
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

  private static string SerializeProperties(Type clrType, IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> val)
  {
    var excluded = EntityAuditMetadata.For(clrType).ExcludedProperties;
    var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var p in properties)
    {
      if (excluded.Contains(p.Metadata.Name))
      {
        continue;
      }

      dict[p.Metadata.Name] = val(p);
    }

    return System.Text.Json.JsonSerializer.Serialize(dict, AuditJsonOptions);
  }

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

        if (entry.Entity is IInheritedByChildTenants existingInherited && tenantId.HasValue)
        {
          var origInheritedTid = entry.Property(nameof(IInheritedByChildTenants.TenantId)).OriginalValue as long? ?? existingInherited.TenantId;
          if (origInheritedTid is { } ownerTid && ownerTid != tenantId.Value)
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

        if (entry.Entity is IInheritedByChildTenants inherited && inherited.TenantId == 0)
        {
          inherited.TenantId = tenantId;
        }

        if (entry.Entity is IHasVersion version && version.Ver == 0)
        {
          version.Ver = 1;
        }
      }
      else if (entry.State == EntityState.Modified)
      {
        var keepsVersion = ChangesLoginCountersOnly(entry) || ChangesKeepsVersionOnly(entry);

        if (entry.Entity is IHasTimestamps timestamps)
        {
          entry.Property(nameof(IHasTimestamps.CreatedAt)).IsModified = false;
          if (!keepsVersion)
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

        if (entry.Entity is IHasVersion version)
        {
          if (keepsVersion)
          {
            entry.Property(nameof(IHasVersion.Ver)).IsModified = false;
          }
          else
          {
            var origVer = entry.Property(nameof(IHasVersion.Ver)).OriginalValue as int? ?? version.Ver;
            version.Ver = origVer + 1;
          }
        }
      }
    }
  }

  private sealed class LocalDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
    v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified),
    v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));

  private sealed class UtcOffsetConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, DateTimeOffset>(
    v => v.ToUniversalTime(),
    v => v.ToUniversalTime());
}
