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

  public override int SaveChanges(bool acceptAllChangesOnSuccess)
  {
    ApplyEntityConventions();
    return base.SaveChanges(acceptAllChangesOnSuccess);
  }

  public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ApplyEntityConventions();

    var pendingEntries = ChangeTracker.Entries()
      .Where(e => e.Entity is IEntity and not Entities.AppPlatformChange && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
      .ToList();

    if (pendingEntries.Count == 0)
    {
      return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    var now = DateTimeOffset.UtcNow;
    var runId = Guid.NewGuid();
    var tenantId = TenantContext.TenantId ?? 0;

    var journalSnapshots = new List<(EntityEntry Entry, Entities.AppPlatformChange Row)>();
    foreach (var entry in pendingEntries)
    {
      var table = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
      var row = new Entities.AppPlatformChange
      {
        RunId = runId,
        At = now,
        TenantId = entry.Entity is IHasTenant t && t.TenantId != 0 ? t.TenantId : tenantId,
        Table = table
      };

      if (entry.State == EntityState.Added)
      {
        row.Operation = Entities.ChangeOperations.Added;
      }
      else if (entry.State == EntityState.Modified)
      {
        row.Operation = Entities.ChangeOperations.Modified;
        row.RowId = ((IEntity)entry.Entity).Id;
        var modified = entry.Properties.Where(p => p.IsModified).ToList();
        if (entry.Entity is IHasVersion)
        {
          modified.Add(entry.Property(nameof(IHasVersion.Ver)));
        }
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

    var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

    try
    {
      var journalRows = new List<Entities.AppPlatformChange>();
      foreach (var (entry, row) in journalSnapshots)
      {
        if (row.Operation == Entities.ChangeOperations.Added)
        {
          row.RowId = ((IEntity)entry.Entity).Id;
          row.After = SerializeProperties(entry.Properties, p => p.CurrentValue);
        }
        journalRows.Add(row);
      }

      if (journalRows.Count > 0)
      {
        AppPlatformChange.AddRange(journalRows);
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var r in journalRows)
        {
          Entry(r).State = EntityState.Detached;
        }
      }
    }
    catch
    {
      // Non-fatal if table not yet created
    }

    return result;
  }

  private static string SerializeProperties(IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> val)
  {
    var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var p in properties)
    {
      if (p.Metadata.Name is nameof(IHasTimestamps.UpdatedAt)) continue;
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
        if (entry.Entity is IHasTimestamps timestamps)
        {
          timestamps.UpdatedAt = now;
        }

        if (entry.Entity is IHasTenant)
        {
          entry.Property(nameof(IHasTenant.TenantId)).IsModified = false;
        }

        if (entry.Entity is IHasVersion version)
        {
          version.Ver++;
        }
      }
    }
  }
}
