using Air.App.Grid;
using Air.App.Modules;
using Air.Data;
using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

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
      .ToListAsync(cancellationToken);

    foreach (var change in changes)
    {
      // Restore record logic
    }

    return new GridActionDto { Outcome = MutationOutcome.Committed };
  }
}
