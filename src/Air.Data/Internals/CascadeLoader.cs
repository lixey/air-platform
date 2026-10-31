using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Platform.Data.Internals;

internal static class CascadeLoader
{
  private const int KeysPerQuery = 500;
  private const int MaxCascadeRows = 2000;

  private static readonly MethodInfo LoadReferencingAsyncMethod =
    typeof(CascadeLoader).GetMethod(nameof(LoadReferencingCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  private static readonly MethodInfo LoadReferencingSyncMethod =
    typeof(CascadeLoader).GetMethod(nameof(LoadReferencingCore), BindingFlags.NonPublic | BindingFlags.Static)!;

  public static async Task LoadDependentsAsync(DbContext db, CancellationToken cancellationToken)
  {
    if (!db.ChangeTracker.Entries().Any(e => e.State == EntityState.Deleted))
    {
      return;
    }

    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var totalLoaded = 0;
    while (totalLoaded < MaxCascadeRows)
    {
      var principals = db.ChangeTracker.Entries()
        .Where(e => e.State == EntityState.Deleted && EntityAuditMetadata.For(e.Metadata.ClrType).IsEntityAudited)
        .Where(e => visited.Add(e.Entity))
        .ToList();

      if (principals.Count == 0)
      {
        return;
      }

      var loadedCount = 0;
      foreach (var (foreignKey, propIndex, keys) in CollectReferencingKeys(principals))
      {
        var remaining = MaxCascadeRows - totalLoaded - loadedCount;
        if (remaining <= 0)
        {
          break;
        }

        var member = foreignKey.Properties[propIndex];
        var task = (Task<int>)LoadReferencingAsyncMethod
          .MakeGenericMethod(foreignKey.DeclaringEntityType.ClrType, member.ClrType)
          .Invoke(null, [db, member.Name, keys, remaining, cancellationToken])!;
        loadedCount += await task;
      }

      if (loadedCount == 0)
      {
        return;
      }

      totalLoaded += loadedCount;
      db.ChangeTracker.CascadeChanges();
    }
  }

  public static void LoadDependents(DbContext db)
  {
    if (!db.ChangeTracker.Entries().Any(e => e.State == EntityState.Deleted))
    {
      return;
    }

    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
    var totalLoaded = 0;
    while (totalLoaded < MaxCascadeRows)
    {
      var principals = db.ChangeTracker.Entries()
        .Where(e => e.State == EntityState.Deleted && EntityAuditMetadata.For(e.Metadata.ClrType).IsEntityAudited)
        .Where(e => visited.Add(e.Entity))
        .ToList();

      if (principals.Count == 0)
      {
        return;
      }

      var loadedCount = 0;
      foreach (var (foreignKey, propIndex, keys) in CollectReferencingKeys(principals))
      {
        var remaining = MaxCascadeRows - totalLoaded - loadedCount;
        if (remaining <= 0)
        {
          break;
        }

        var member = foreignKey.Properties[propIndex];
        loadedCount += (int)LoadReferencingSyncMethod
          .MakeGenericMethod(foreignKey.DeclaringEntityType.ClrType, member.ClrType)
          .Invoke(null, [db, member.Name, keys, remaining])!;
      }

      if (loadedCount == 0)
      {
        return;
      }

      totalLoaded += loadedCount;
      db.ChangeTracker.CascadeChanges();
    }
  }

  private static List<(IForeignKey ForeignKey, int PropertyIndex, List<object> Keys)> CollectReferencingKeys(List<EntityEntry> principals)
  {
    var byFk = new Dictionary<IForeignKey, (int PropertyIndex, List<object> Keys)>();
    foreach (var principal in principals)
    {
      foreach (var fk in principal.Metadata.GetReferencingForeignKeys())
      {
        if (fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull) ||
            fk.DeclaringEntityType.IsOwned() ||
            fk.DeclaringEntityType.HasSharedClrType ||
            !EntityAuditMetadata.For(fk.DeclaringEntityType.ClrType).IsEntityAudited ||
            ReferencePropertyIndex(fk) is not { } propIndex)
        {
          continue;
        }

        var principalPropName = fk.PrincipalKey.Properties[propIndex].Name;
        if (principal.Property(principalPropName).OriginalValue is { } key)
        {
          if (!byFk.TryGetValue(fk, out var bucket))
          {
            bucket = (propIndex, []);
            byFk[fk] = bucket;
          }

          bucket.Keys.Add(key);
        }
      }
    }

    return byFk.Select(kv => (kv.Key, kv.Value.PropertyIndex, kv.Value.Keys)).ToList();
  }

  private static int? ReferencePropertyIndex(IForeignKey fk) =>
    fk.Properties.Count switch
    {
      1 => 0,
      2 when fk.Properties[0].Name == nameof(IHasTenant.TenantId) => 1,
      2 when fk.Properties[1].Name == nameof(IHasTenant.TenantId) => 0,
      _ => null
    };

  private static async Task<int> LoadReferencingCoreAsync<TEntity, TKey>(
    DbContext db,
    string property,
    List<object> keys,
    int maxRows,
    CancellationToken cancellationToken)
    where TEntity : class
  {
    var count = 0;
    foreach (var chunk in keys.Distinct().Chunk(KeysPerQuery))
    {
      var take = maxRows - count;
      if (take <= 0)
      {
        break;
      }

      var values = chunk.Select(k => (TKey)k).ToList();
      var rows = await db.Set<TEntity>().AsTracking()
        .Where(e => values.Contains(EF.Property<TKey>(e, property)))
        .Take(take)
        .ToListAsync(cancellationToken);
      count += rows.Count;
    }

    return count;
  }

  private static int LoadReferencingCore<TEntity, TKey>(
    DbContext db,
    string property,
    List<object> keys,
    int maxRows)
    where TEntity : class
  {
    var count = 0;
    foreach (var chunk in keys.Distinct().Chunk(KeysPerQuery))
    {
      var take = maxRows - count;
      if (take <= 0)
      {
        break;
      }

      var values = chunk.Select(k => (TKey)k).ToList();
      var rows = db.Set<TEntity>().AsTracking()
        .Where(e => values.Contains(EF.Property<TKey>(e, property)))
        .Take(take)
        .ToList();
      count += rows.Count;
    }

    return count;
  }
}
