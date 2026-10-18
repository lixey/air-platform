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
public abstract class ChangeGridService(PlatformDbContext db) : GridService<AppPlatformChange>(db)
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
    nav.AddAction(Restore, b => b.Label("Відновити").Icon("reload"), "Відновити вибрані записи?").AllowReadOnly();
  }

  public virtual async Task<GridActionDto> Restore(GridRequest request, CancellationToken cancellationToken)
  {
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

    foreach (var change in changes)
    {
      var entityType = db.Model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == change.Table)?.ClrType;
      if (entityType == null) continue;

      if (change.Operation == ChangeOperations.Modified && !string.IsNullOrEmpty(change.Before))
      {
        var entity = await db.FindAsync(entityType, [change.RowId], cancellationToken);
        if (entity != null)
        {
          var beforeDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(change.Before);
          if (beforeDict != null)
          {
            var entry = db.Entry(entity);
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
            var entry = db.Entry(entity);
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
          }
        }
      }
    }

    await db.SaveChangesAsync(cancellationToken);
    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }
}
