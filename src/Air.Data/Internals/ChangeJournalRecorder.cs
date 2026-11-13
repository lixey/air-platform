using System.Text.Encodings.Web;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Platform.Data.Internals;

internal static class ChangeJournalRecorder
{
  private static readonly JsonSerializerOptions AuditJsonOptions = new()
  {
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

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
                                     !Equals(p.OriginalValue, p.CurrentValue) &&
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
      var table = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name;
      var rowTenantId = entry.Entity switch
      {
        IHasTenant t when t.TenantId > 0 => (long?)t.TenantId,
        IHasOptionalTenant ot => ot.TenantId is > 0 ? ot.TenantId : null,
        _ => contextTenantId
      };
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
          Operation = ChangeOperations.Added
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
            Operation = ChangeOperations.Modified
          };
          run.JournalEntries[entry.Entity] = state;
        }

        foreach (var p in entry.Properties)
        {
          if (excluded.Contains(p.Metadata.Name))
          {
            continue;
          }

          if (Equals(p.OriginalValue, p.CurrentValue))
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
            Operation = ChangeOperations.Deleted,
            IsDeleted = true
          };
          run.JournalEntries[entry.Entity] = state;
        }
        else
        {
          state.Operation = ChangeOperations.Deleted;
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
    ITenantContext tenantContext)
  {
    if (run.JournalEntries.Count == 0)
    {
      return [];
    }

    var now = TimeProvider.System.GetUtcNow();
    var userId = tenantContext.UserId;
    var correlationId = tenantContext.CorrelationId;
    var rows = new List<AppPlatformChange>(run.JournalEntries.Count);

    foreach (var state in run.JournalEntries.Values)
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
          .Where(prop => !Equals(state.FirstBefore.GetValueOrDefault(prop), state.LatestAfter.GetValueOrDefault(prop)))
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
}
