using Air.App.Components;
using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Air.App.Changes;

/// <summary>
/// The change journal (AppPlatformChange) of the tenant: a grid an application routes with a sealed
/// service of its own and a read-only feature.
/// </summary>
public abstract class ChangeGridService(PlatformDbContext db, IServiceProvider? services = null, PlatformAppService? app = null) : GridService<AppPlatformChange>(db, app)
{
  protected override void SetColumns(Columns<AppPlatformChange> columns)
  {
    columns.Add(r => r.At).AsDatetime();
    columns.Add(r => r.UserId);
    columns.Add(r => r.Operation);
    columns.Add(r => r.Table);
    columns.Add(r => r.RowId);
    columns.Add(r => r.Before);
    columns.Add(r => r.After);
    columns.Add(r => r.CorrelationId);
  }

  protected override void SetQuery(Query query)
  {
    query.SortByDescending(r => r.At);
    query.SearchBy(r => r.Table, r => r.Operation, r => r.CorrelationId);
  }

  protected override void SetNav(Nav nav)
  {
    base.SetNav(nav);
    nav.AddAction(Restore, b => b.Label(L["Restore"]).Icon(UiIcon.Reload), L["RestoreConfirm"]).AllowReadOnly();
  }

  public virtual async Task<GridActionDto> Restore(GridRequest request, CancellationToken cancellationToken)
  {
    if (User != null && User.IsAuthenticated &&
        !User.Can(Feature.Action(nameof(Restore))) &&
        !User.Can(Feature.Edit) &&
        !User.Can(Feature.All) &&
        !User.Can("*"))
    {
      return new GridActionDto { Outcome = MutationOutcome.Refused };
    }

    var ids = request.SelectedIds ?? [];
    if (ids.Count == 0 && request.Id.HasValue) ids = [request.Id.Value];

    if (ids.Count == 0)
    {
      return new GridActionDto { Outcome = MutationOutcome.Committed };
    }

    var changes = await db.Set<AppPlatformChange>()
      .Where(c => ids.Contains(c.Id))
      .OrderByDescending(c => c.Id)
      .ToListAsync(cancellationToken);

    var modifiedContexts = new HashSet<PlatformDbContext>();

    foreach (var change in changes)
    {
      var targetDb = GetDbContextForTable(change.Table);
      var entityType = targetDb.Model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == change.Table || e.GetTableName() == change.Table)?.ClrType;
      if (entityType == null) continue;

      if (change.Operation == ChangeOperations.Modified && !string.IsNullOrEmpty(change.Before))
      {
        var entity = await targetDb.FindAsync(entityType, [change.RowId], cancellationToken);
        if (entity != null)
        {
          var beforeDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(change.Before);
          if (beforeDict != null)
          {
            var entry = targetDb.Entry(entity);
            foreach (var kvp in beforeDict)
            {
              if (kvp.Key == "Ver") continue;
              try
              {
                var prop = entry.Property(kvp.Key);
                if (prop != null && !prop.Metadata.IsPrimaryKey())
                {
                  var targetType = Nullable.GetUnderlyingType(prop.Metadata.ClrType) ?? prop.Metadata.ClrType;
                  object? typedVal = null;
                  if (kvp.Value is JsonElement je)
                  {
                    typedVal = JsonSerializer.Deserialize(je.GetRawText(), prop.Metadata.ClrType);
                  }
                  else if (kvp.Value != null)
                  {
                    typedVal = Convert.ChangeType(kvp.Value, targetType);
                  }
                  prop.CurrentValue = typedVal;
                }
              }
              catch { }
            }
            modifiedContexts.Add(targetDb);
          }
        }
      }
      else if (change.Operation == ChangeOperations.Deleted && !string.IsNullOrEmpty(change.Before))
      {
        var beforeDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(change.Before);
        if (beforeDict != null)
        {
          var entity = Activator.CreateInstance(entityType);
          if (entity != null)
          {
            var entry = targetDb.Entry(entity);
            foreach (var kvp in beforeDict)
            {
              try
              {
                var prop = entry.Property(kvp.Key);
                if (prop != null)
                {
                  var targetType = Nullable.GetUnderlyingType(prop.Metadata.ClrType) ?? prop.Metadata.ClrType;
                  object? typedVal = null;
                  if (kvp.Value is JsonElement je)
                  {
                    typedVal = JsonSerializer.Deserialize(je.GetRawText(), prop.Metadata.ClrType);
                  }
                  else if (kvp.Value != null)
                  {
                    typedVal = Convert.ChangeType(kvp.Value, targetType);
                  }
                  prop.CurrentValue = typedVal;
                }
              }
              catch { }
            }
            entry.State = EntityState.Added;
            modifiedContexts.Add(targetDb);
          }
        }
      }
    }

    foreach (var ctx in modifiedContexts)
    {
      await ctx.SaveChangesAsync(cancellationToken);
    }

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }

  private PlatformDbContext GetDbContextForTable(string table)
  {
    if (db.Model.GetEntityTypes().Any(e => e.ClrType.Name == table || e.GetTableName() == table))
    {
      return db;
    }
    var sp = services ?? PlatformApp?.Services;
    if (sp != null)
    {
      var dbTypes = AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
        .Where(t => typeof(PlatformDbContext).IsAssignableFrom(t) && !t.IsAbstract && t != db.GetType());

      foreach (var dbType in dbTypes)
      {
        if (sp.GetService(dbType) is PlatformDbContext otherDb)
        {
          if (otherDb.Model.GetEntityTypes().Any(e => e.ClrType.Name == table || e.GetTableName() == table))
          {
            return otherDb;
          }
        }
      }
    }
    return db;
  }
}
