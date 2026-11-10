using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Platform.Data.Internals;

internal static class EntityWriteConventions
{
  public static void Apply(
    ChangeTracker changeTracker,
    ITenantContext tenantContext,
    MutationRunScope? activeMutationRun,
    bool suppressDerivedAudit)
  {
    var now = DateTimeOffset.UtcNow;
    var tenantId = tenantContext.TenantId;
    var isSystemScope = tenantContext.IsSystemScope;

    foreach (var entry in changeTracker.Entries())
    {
      if (entry.Entity is AppPlatformChange && entry.State is EntityState.Modified or EntityState.Deleted)
      {
        throw new InvalidOperationException("AppPlatformChange rows are immutable and cannot be modified or deleted.");
      }

      if (entry.State is EntityState.Modified or EntityState.Deleted)
      {
        if (entry.Entity is IHasTenant existingTenant && !isSystemScope)
        {
          if (!tenantId.HasValue)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete {entry.Metadata.ClrType.Name} without an active tenant.");
          }

          var origTid = entry.Property(nameof(IHasTenant.TenantId)).OriginalValue as long? ?? existingTenant.TenantId;
          if (origTid != tenantId.Value ||
              entry.State == EntityState.Modified && existingTenant.TenantId != 0 && existingTenant.TenantId != tenantId.Value)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete {entry.Metadata.ClrType.Name} belonging to another tenant.");
          }
        }

        if (entry.Entity is IInheritedByChildTenants existingInherited && !isSystemScope)
        {
          if (!tenantId.HasValue)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete inherited {entry.Metadata.ClrType.Name} without an active tenant.");
          }

          var origInheritedTid = entry.Property(nameof(IInheritedByChildTenants.TenantId)).OriginalValue as long?;
          if (origInheritedTid != tenantId.Value ||
              entry.State == EntityState.Modified && existingInherited.TenantId != tenantId.Value)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete inherited {entry.Metadata.ClrType.Name} belonging to another tenant.");
          }
        }
      }

      if (entry.State is EntityState.Added or EntityState.Modified)
      {
        NormalizeWallClockDates(entry);
      }

      if (entry.State == EntityState.Added)
      {
        if (entry.Entity is IHasTimestamps timestamps)
        {
          timestamps.CreatedAt = now;
        }

        if (entry.Entity is IHasTenant tenantEntity)
        {
          if (!isSystemScope)
          {
            if (!tenantId.HasValue)
            {
              throw new InvalidOperationException(
                $"Cannot insert {entry.Metadata.ClrType.Name} without an active tenant.");
            }

            if (tenantEntity.TenantId == 0)
            {
              tenantEntity.TenantId = tenantId.Value;
            }
            else if (tenantEntity.TenantId != tenantId.Value)
            {
              throw new InvalidOperationException(
                $"Cannot insert {entry.Metadata.ClrType.Name} belonging to another tenant.");
            }
          }
          else if (tenantEntity.TenantId == 0)
          {
            tenantEntity.TenantId = tenantId ?? throw new InvalidOperationException(
              $"Cannot insert {entry.Metadata.ClrType.Name} without an explicit TenantId.");
          }
        }

        if (entry.Entity is IInheritedByChildTenants inherited)
        {
          if (!isSystemScope)
          {
            if (!tenantId.HasValue)
            {
              throw new InvalidOperationException(
                $"Cannot insert inherited {entry.Metadata.ClrType.Name} without an active tenant.");
            }

            if (inherited.TenantId is null or 0)
            {
              inherited.TenantId = tenantId.Value;
            }
            else if (inherited.TenantId != tenantId.Value)
            {
              throw new InvalidOperationException(
                $"Cannot insert inherited {entry.Metadata.ClrType.Name} belonging to another tenant.");
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

          activeMutationRun?.VersionIncrementedEntities.Add(entry.Entity);
        }
      }
      else if (entry.State == EntityState.Modified)
      {
        var noAuditOnly = ChangesNoAuditOnly(entry, suppressDerivedAudit);

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

        if (entry.Entity is IInheritedByChildTenants && !isSystemScope)
        {
          var tenantProp = entry.Property(nameof(IInheritedByChildTenants.TenantId));
          tenantProp.CurrentValue = tenantProp.OriginalValue;
          tenantProp.IsModified = false;
        }

        if (entry.Entity is IHasVersion version)
        {
          var verProp = entry.Property(nameof(IHasVersion.Ver));
          if (noAuditOnly || activeMutationRun != null && !activeMutationRun.VersionIncrementedEntities.Add(entry.Entity))
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

  private static void NormalizeWallClockDates(EntityEntry entry)
  {
    foreach (var prop in entry.Properties)
    {
      if (entry.State != EntityState.Added && !prop.IsModified)
      {
        continue;
      }

      if (prop.CurrentValue is DateTime dt && dt.Kind != DateTimeKind.Unspecified)
      {
        var unspecified = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        if (prop.Metadata.PropertyInfo is { CanWrite: true } clrProperty)
        {
          clrProperty.SetValue(entry.Entity, unspecified);
        }
        else
        {
          prop.CurrentValue = unspecified;
        }
      }
    }
  }

  private static bool ChangesNoAuditOnly(EntityEntry entry, bool suppressDerivedAudit)
  {
    if (entry.State != EntityState.Modified)
    {
      return false;
    }

    if (suppressDerivedAudit)
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
}
