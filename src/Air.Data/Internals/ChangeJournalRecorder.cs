using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Platform.Data.Internals;

internal static class ChangeJournalRecorder
{
  private static readonly JsonSerializerOptions AuditJsonOptions = new()
  {
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  internal static bool PropertyValuesEqual(IProperty? property, object? left, object? right)
  {
    if (ReferenceEquals(left, right))
    {
      return true;
    }

    if (left is null || right is null)
    {
      return false;
    }

    if (property?.GetValueComparer() is { } comparer &&
        left.GetType() == right.GetType() &&
        comparer.Type.IsAssignableFrom(left.GetType()))
    {
      return comparer.Equals(left, right);
    }

    if (left is byte[] leftBytes && right is byte[] rightBytes)
    {
      return leftBytes.AsSpan().SequenceEqual(rightBytes);
    }

    if (left is Array && right is Array && left.GetType() == right.GetType())
    {
      return StructuralComparisons.StructuralEqualityComparer.Equals(left, right);
    }

    return Equals(left, right);
  }

  public static bool IsJournaled(EntityEntry entry, bool suppressAllAudit, bool suppressDerivedAudit)
  {
    if (suppressAllAudit || suppressDerivedAudit && entry.State == EntityState.Modified)
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
                                     !PropertyValuesEqual(p.Metadata, p.OriginalValue, p.CurrentValue) &&
                                     p.Metadata.Name != nameof(IHasVersion.Ver) &&
                                     !auditMeta.ExcludedProperties.Contains(p.Metadata.Name));
  }

  public static List<MutationRunEntryState> CaptureRunFlushStates(
    ChangeTracker changeTracker,
    ITenantContext tenantContext,
    MutationRunScope run,
    bool suppressAllAudit,
    bool suppressDerivedAudit)
  {
    var pendingEntries = changeTracker.Entries()
      .Where(e => IsJournaled(e, suppressAllAudit, suppressDerivedAudit) &&
                  e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
      .ToList();

    if (pendingEntries.Count == 0)
    {
      return [];
    }

    var contextTenantId = tenantContext.TenantId;
    var toCompleteAfterFlush = new List<MutationRunEntryState>(pendingEntries.Count);

    foreach (var entry in pendingEntries)
    {
      var clrType = entry.Metadata.ClrType;
      var entityId = (entry.Entity as IEntity)?.Id ?? 0;
      var table = entry.Metadata.GetTableName() ?? clrType.Name;
      var rowTenantId = entry.Entity switch
      {
        IHasTenant t when t.TenantId > 0 => (long?)t.TenantId,
        IHasOptionalTenant ot => ot.TenantId is > 0 ? ot.TenantId : null,
        _ => contextTenantId
      };
      var excluded = EntityAuditMetadata.For(clrType).ExcludedProperties;

      MutationRunEntryState? state = null;
      if (entityId > 0)
      {
        run.JournalEntriesByKey.TryGetValue((clrType, entityId), out state);
      }

      state ??= run.JournalEntriesByRef.GetValueOrDefault(entry.Entity);

      if (entry.State == EntityState.Added)
      {
        state = new MutationRunEntryState
        {
          Entry = entry,
          Run = run,
          ClrType = clrType,
          Table = table,
          TenantId = rowTenantId,
          RowId = entityId,
          IsAdded = true,
          Operation = ChangeOperations.Added
        };
        RegisterState(run, entry.Entity, entityId, state);
        toCompleteAfterFlush.Add(state);
      }
      else if (entry.State == EntityState.Modified)
      {
        if (state is { IsAdded: true })
        {
          state.Entry = entry;
          run.JournalEntriesByRef[entry.Entity] = state;
          toCompleteAfterFlush.Add(state);
          continue;
        }

        if (state == null)
        {
          state = new MutationRunEntryState
          {
            Entry = entry,
            Run = run,
            ClrType = clrType,
            Table = table,
            TenantId = rowTenantId,
            RowId = entityId,
            Operation = ChangeOperations.Modified
          };
          RegisterState(run, entry.Entity, entityId, state);
        }
        else
        {
          state.Entry = entry;
          run.JournalEntriesByRef[entry.Entity] = state;
        }

        foreach (var p in entry.Properties)
        {
          if (excluded.Contains(p.Metadata.Name))
          {
            continue;
          }

          if (PropertyValuesEqual(p.Metadata, p.OriginalValue, p.CurrentValue))
          {
            if (state.TouchedProperties.Contains(p.Metadata.Name))
            {
              state.LatestAfter[p.Metadata.Name] = p.CurrentValue;
            }

            continue;
          }

          if (!p.IsModified)
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
          RemoveState(run, entry.Entity, entityId, state);
          continue;
        }

        if (state == null)
        {
          state = new MutationRunEntryState
          {
            Entry = entry,
            Run = run,
            ClrType = clrType,
            Table = table,
            TenantId = rowTenantId,
            RowId = entityId,
            Operation = ChangeOperations.Deleted,
            IsDeleted = true
          };
          RegisterState(run, entry.Entity, entityId, state);
        }
        else
        {
          state.Entry = entry;
          state.Operation = ChangeOperations.Deleted;
          state.IsDeleted = true;
          run.JournalEntriesByRef[entry.Entity] = state;
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

  public static void CompleteRunFlushStates(List<MutationRunEntryState> states)
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
        if (state.RowId > 0 && state.Run != null)
        {
          state.Run.JournalEntriesByKey[(state.ClrType, state.RowId)] = state;
        }

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

  public static List<AppPlatformChange> BuildConsolidatedJournalRows(
    MutationRunScope run,
    ITenantContext tenantContext,
    TimeProvider? timeProvider = null)
  {
    if (run.JournalEntries.Count == 0)
    {
      return [];
    }

    var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
    var userId = tenantContext.UserId;
    var correlationId = tenantContext.CorrelationId;
    var rows = new List<AppPlatformChange>(run.JournalEntries.Count);

    foreach (var state in run.JournalEntries)
    {
      var row = new AppPlatformChange
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
        if (state.Entry.State != EntityState.Detached)
        {
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

        row.After = JsonSerializer.Serialize(state.LatestAfter, AuditJsonOptions);
        rows.Add(row);
      }
      else if (state.Operation == ChangeOperations.Deleted)
      {
        row.Before = JsonSerializer.Serialize(state.FirstBefore, AuditJsonOptions);
        rows.Add(row);
      }
      else
      {
        var changedProps = state.TouchedProperties
          .Where(prop =>
          {
            var propMeta = state.Entry.Metadata.FindProperty(prop);
            return !PropertyValuesEqual(propMeta, state.FirstBefore.GetValueOrDefault(prop), state.LatestAfter.GetValueOrDefault(prop));
          })
          .ToList();
        if (!changedProps.Any(prop =>
              prop is not (nameof(IHasTimestamps.UpdatedAt) or nameof(IHasTimestamps.CreatedAt) or nameof(IHasVersion.Ver))))
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

        row.Before = JsonSerializer.Serialize(beforeDict, AuditJsonOptions);
        row.After = JsonSerializer.Serialize(afterDict, AuditJsonOptions);
        rows.Add(row);
      }
    }

    return rows;
  }

  private static void RegisterState(MutationRunScope run, object entity, long entityId, MutationRunEntryState state)
  {
    run.JournalEntries.Add(state);
    run.JournalEntriesByRef[entity] = state;
    if (entityId > 0)
    {
      run.JournalEntriesByKey[(state.ClrType, entityId)] = state;
    }
  }

  private static void RemoveState(MutationRunScope run, object entity, long entityId, MutationRunEntryState state)
  {
    run.JournalEntries.Remove(state);
    run.JournalEntriesByRef.Remove(state.Entry.Entity);
    run.JournalEntriesByRef.Remove(entity);
    if (state.RowId > 0)
    {
      run.JournalEntriesByKey.Remove((state.ClrType, state.RowId));
    }

    if (entityId > 0)
    {
      run.JournalEntriesByKey.Remove((state.ClrType, entityId));
    }
  }
}
