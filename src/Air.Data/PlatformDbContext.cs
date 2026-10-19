using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Air.Data;

/// <summary>
/// Base EF Core DbContext for the platform and its modules.
/// Automates tenant isolation (IHasTenant), audit timestamps (IHasTimestamps),
/// and optimistic concurrency versioning (IHasVersion).
/// </summary>
public abstract class PlatformDbContext : DbContext
{
  private readonly ITenantContext? _tenantContext;

  protected PlatformDbContext()
  {
  }

  protected PlatformDbContext(DbContextOptions options) : base(options)
  {
  }

  protected PlatformDbContext(DbContextOptions options, ITenantContext? tenantContext) : base(options)
  {
    _tenantContext = tenantContext;
  }

  public ITenantContext TenantContext => _tenantContext ?? AsyncLocalTenantContext.Instance;

  public DbSet<AppLanguage> AppLanguage => Set<AppLanguage>();
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
        var currentTenantId = Expression.Property(tenantIdExpr, nameof(ITenantContext.TenantId));

        // Filter: TenantContext.TenantId == null || EF.Property<long>(e, "TenantId") == TenantContext.TenantId
        var nullCheck = Expression.Equal(currentTenantId, Expression.Constant(null, typeof(long?)));
        var convertedProperty = Expression.Convert(property, typeof(long?));
        var equality = Expression.Equal(convertedProperty, currentTenantId);
        var filterBody = Expression.OrElse(nullCheck, equality);

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
        var currentTenantId = Expression.Property(tenantIdExpr, nameof(ITenantContext.TenantId));

        // Filter: TenantContext.TenantId == null || EF.Property<long?>(e, "TenantId") == null || EF.Property<long?>(e, "TenantId") == TenantContext.TenantId
        var nullContextCheck = Expression.Equal(currentTenantId, Expression.Constant(null, typeof(long?)));
        var nullTenantCheck = Expression.Equal(property, Expression.Constant(null, typeof(long?)));
        var equality = Expression.Equal(property, currentTenantId);
        var filterBody = Expression.OrElse(nullContextCheck, Expression.OrElse(nullTenantCheck, equality));

        var lambda = Expression.Lambda(filterBody, parameter);
        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
        modelBuilder.Entity(clrType).Property(nameof(IInheritedByChildTenants.TenantId)).IsConcurrencyToken();
      }

        if (typeof(IHasVersion).IsAssignableFrom(clrType))
      {
        modelBuilder.Entity(clrType).Property(nameof(IHasVersion.Ver)).IsConcurrencyToken();
      }
    }
  }

  protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
  {
    base.ConfigureConventions(configurationBuilder);
    configurationBuilder.Properties<DateTime>().HaveConversion<LocalDateTimeConverter>();
    configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcOffsetConverter>();
  }

  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
    ChangeTracker.DetectChanges();
    ApplyEntityConventions();

    var journalSnapshots = CaptureJournalSnapshots();
    var result = base.SaveChanges(acceptAllChangesOnSuccess);
    if (journalSnapshots.Count > 0)
    {
      var journalRows = CompleteJournalRows(journalSnapshots);
      try
      {
        AppPlatformChange.AddRange(journalRows);
        base.SaveChanges(acceptAllChangesOnSuccess);
      }
      catch
      {
        // Non-fatal if table not yet created
      }
      finally
      {
        DetachJournalRows(journalRows);
      }
    }

    return result;
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

    var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    var journalRows = CompleteJournalRows(journalSnapshots);
    try
    {
      if (journalRows.Count > 0)
      {
        AppPlatformChange.AddRange(journalRows);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
      }
    }
    catch
    {
      // Non-fatal if table not yet created
    }
    finally
    {
      DetachJournalRows(journalRows);
    }

    return result;
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
        var modified = entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
        if (modified.Count == 0) continue;
        row.Operation = isRestored ? Entities.ChangeOperations.Restored : Entities.ChangeOperations.Modified;
        row.RowId = ((IEntity)entry.Entity).Id;
        row.Before = SerializeProperties(modified, p => p.OriginalValue);
        row.After = SerializeProperties(modified, p => p.CurrentValue);
      }
      else if (entry.State == EntityState.Deleted)
      {
        row.Operation = Entities.ChangeOperations.Deleted;
        row.RowId = ((IEntity)entry.Entity).Id;
        row.Before = SerializeProperties(entry.Properties, p => p.OriginalValue);
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
        row.After = SerializeProperties(entry.Properties, p => p.CurrentValue);
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

  private static bool IsJournaled(EntityEntry entry) =>
    entry.Entity is IEntity and not Entities.AppPlatformChange and not BaseAppAuthSession &&
    !ChangesLoginCountersOnly(entry) &&
    (entry.State != EntityState.Modified ||
     entry.Properties.Any(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue) && p.Metadata.Name is not nameof(IHasTimestamps.UpdatedAt) and not nameof(IHasVersion.Ver)));

  private static bool ChangesLoginCountersOnly(EntityEntry entry) =>
    entry.Entity is BaseAppUser &&
    entry.State == EntityState.Modified &&
    entry.Properties.Any(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)) &&
    entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).All(p => BaseAppUser.LoginCounters.Contains(p.Metadata.Name));

  private static string SerializeProperties(IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> val)
  {
    var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var p in properties)
    {
      if (p.Metadata.Name is nameof(IHasTimestamps.UpdatedAt)) continue;
      if (((MemberInfo?)p.Metadata.PropertyInfo ?? p.Metadata.FieldInfo) is { } member &&
          Attribute.IsDefined(member, typeof(SensitiveAttribute), inherit: true))
      {
        continue;
      }
      dict[p.Metadata.Name] = val(p);
    }
    return System.Text.Json.JsonSerializer.Serialize(dict);
  }

  private void ApplyEntityConventions()
  {
    var now = DateTimeOffset.UtcNow;
    var tenantId = TenantContext.TenantId;

    foreach (var entry in ChangeTracker.Entries())
    {
      if (entry.State == EntityState.Added)
      {
        if (entry.Entity is IHasTimestamps timestamps)
        {
          timestamps.CreatedAt = now;
        }

        if (entry.Entity is IHasTenant tenantEntity && tenantEntity.TenantId == 0)
        {
          tenantEntity.TenantId = tenantId ?? throw new InvalidOperationException(
            $"Cannot insert {entry.Metadata.ClrType.Name} without an active tenant."
          );
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
        var keepsVersion = ChangesLoginCountersOnly(entry);

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
          entry.Property(nameof(IHasTenant.TenantId)).IsModified = false;
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
