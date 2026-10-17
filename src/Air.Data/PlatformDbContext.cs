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

  public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
  {
    ApplyEntityConventions();
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
