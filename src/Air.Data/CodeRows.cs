using Air.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Air.Data;

public static class CodeRows
{
  public static async Task SyncAsync<TEntity>(DbContext db, IReadOnlyCollection<TEntity> rows, CancellationToken cancellationToken = default)
    where TEntity : class, IEntity
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(rows);

    var set = db.Set<TEntity>();
    var existing = await set.ToDictionaryAsync(r => r.Id, cancellationToken);

    foreach (var row in rows)
    {
      if (!existing.TryGetValue(row.Id, out var current))
      {
        set.Add(row);
      }
      else
      {
        db.Entry(current).CurrentValues.SetValues(row);
      }
    }

    await db.SaveChangesAsync(cancellationToken);
  }
}
