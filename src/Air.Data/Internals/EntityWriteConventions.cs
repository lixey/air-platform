using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Platform.Data.Internals;

internal static class EntityWriteConventions
{
  public static void Apply(
    ChangeTracker changeTracker,
    ITenantContext tenantContext,
    MutationRunScope? activeMutationRun)
  {
    var now = TimeProvider.System.GetUtcNow();
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

        if (entry.Entity is IHasOptionalTenant existingOptional && !isSystemScope)
        {
          if (!tenantId.HasValue)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete {entry.Metadata.ClrType.Name} without an active tenant.");
          }

          var origOptionalTid = entry.Property(nameof(IHasOptionalTenant.TenantId)).OriginalValue as long?;
          if (origOptionalTid != tenantId.Value ||
              entry.State == EntityState.Modified && existingOptional.TenantId != tenantId.Value)
          {
            throw new InvalidOperationException(
              $"Cannot modify or delete {entry.Metadata.ClrType.Name} belonging to another tenant.");
          }
        }

        if (entry.Entity is AppAgentMemory && !isSystemScope)
        {
          var origUserId = entry.Property(nameof(AppAgentMemory.UserId)).OriginalValue as long?;
          if (origUserId.HasValue && origUserId != tenantContext.UserId)
          {
            throw new InvalidOperationException("Cannot modify or delete another user's AppAgentMemory.");
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

        if (entry.Entity is IHasOptionalTenant optionalTenant)
        {
          if (!isSystemScope)
          {
            if (!tenantId.HasValue)
            {
              throw new InvalidOperationException(
                $"Cannot insert {entry.Metadata.ClrType.Name} without an active tenant.");
            }

            if (optionalTenant.TenantId is null or 0)
            {
              optionalTenant.TenantId = tenantId.Value;
            }
            else if (optionalTenant.TenantId != tenantId.Value)
            {
              throw new InvalidOperationException(
                $"Cannot insert {entry.Metadata.ClrType.Name} belonging to another tenant.");
            }
          }
          else if (optionalTenant.TenantId == 0)
          {
            optionalTenant.TenantId = tenantId;
          }
        }

        if (entry.Entity is AppAgentMemory addedMemory && !isSystemScope)
        {
          if (addedMemory.UserId is null or 0)
          {
            var isSharedKind = string.Equals(addedMemory.Kind, AgentMemoryKinds.Rule, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(addedMemory.Kind, AgentMemoryKinds.Seed, StringComparison.OrdinalIgnoreCase);
            addedMemory.UserId = isSharedKind ? null : tenantContext.UserId;
          }
          else if (addedMemory.UserId != tenantContext.UserId)
          {
            throw new InvalidOperationException("Cannot insert AppAgentMemory belonging to another user.");
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

        if (entry.Entity is IHasOptionalTenant && !isSystemScope)
        {
          var tenantProp = entry.Property(nameof(IHasOptionalTenant.TenantId));
          tenantProp.CurrentValue = tenantProp.OriginalValue;
          tenantProp.IsModified = false;
        }

        if (entry.Entity is AppAgentMemory && !isSystemScope)
        {
          var userProp = entry.Property(nameof(AppAgentMemory.UserId));
          userProp.CurrentValue = userProp.OriginalValue;
          userProp.IsModified = false;
        }

        if (entry.Entity is IHasVersion version)
        {
          var verProp = entry.Property(nameof(IHasVersion.Ver));
          var entityKey = entry.Entity is IEntity keyed && keyed.Id > 0
            ? (entry.Metadata.ClrType, keyed.Id)
            : ((Type, long)?)null;
          var alreadyIncremented = activeMutationRun != null && !noAuditOnly && (
            !activeMutationRun.VersionIncrementedEntities.Add(entry.Entity) ||
            entityKey.HasValue && !activeMutationRun.VersionIncrementedKeys.Add(entityKey.Value));

          if (noAuditOnly)
          {
            if (verProp.OriginalValue is int origV)
            {
              version.Ver = origV;
            }

            verProp.IsModified = false;
          }
          else if (alreadyIncremented)
          {
            if (entityKey.HasValue && activeMutationRun!.InitialVersions.TryGetValue(entityKey.Value, out var initVer))
            {
              var targetVer = initVer + 1;
              version.Ver = targetVer;
              verProp.IsModified = verProp.OriginalValue is not int curOrig || curOrig != targetVer;
            }
            else
            {
              if (verProp.OriginalValue is int origV)
              {
                version.Ver = origV;
              }

              verProp.IsModified = false;
            }
          }
          else
          {
            var origVer = verProp.OriginalValue as int? ?? version.Ver;
            if (activeMutationRun != null && entityKey.HasValue)
            {
              activeMutationRun.InitialVersions.TryAdd(entityKey.Value, origVer);
            }

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

  private static bool ChangesNoAuditOnly(EntityEntry entry)
  {
    if (entry.State != EntityState.Modified)
    {
      return false;
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
