namespace Platform.App.Files.Internals;

internal static class FileStorageCleaner
{
  private static readonly MethodInfo EnumerableContainsLongMethod =
    typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
      .Single(static m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
      .MakeGenericMethod(typeof(long));

  private static readonly MethodInfo EnumerableContainsNullableLongMethod =
    typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
      .Single(static m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
      .MakeGenericMethod(typeof(long?));

  public static IFileStores ResolveStores(IServiceProvider? services) =>
    services?.GetService<IFileStores>()
    ?? (services?.GetService<IFileStorage>() is { } storage
      ? new DefaultFileStores(storage)
      : new DefaultFileStores(new LocalFileStorage()));

  public static List<(string? Store, string Key)> CollectKeys<TFile>(IEnumerable<TFile> files)
    where TFile : BaseAppFile
  {
    var rules = FileRulesAttribute.Of(typeof(TFile));
    var defaultStoreKey = rules.StoreType != null ? FileStore.KeyOf(rules.StoreType) : null;
    var keys = new List<(string? Store, string Key)>();

    foreach (var item in files)
    {
      var itemStore = item.Store ?? defaultStoreKey;
      if (!string.IsNullOrEmpty(item.StorageKey))
      {
        keys.Add((itemStore, item.StorageKey));
      }

      if (!string.IsNullOrEmpty(item.ThumbnailKey))
      {
        keys.Add((itemStore, item.ThumbnailKey));
      }
    }

    return keys;
  }

  public static async Task DeleteUnreferencedBlobsAsync<TFile>(
    PlatformDbContext db,
    IFileStores stores,
    IReadOnlyCollection<(string? Store, string Key)> keysToDelete,
    CancellationToken ct)
    where TFile : BaseAppFile
  {
    if (keysToDelete.Count == 0)
    {
      return;
    }

    try
    {
      var distinctKeys = keysToDelete.Select(static x => x.Key).Distinct(StringComparer.Ordinal).ToList();
      var referencedQuery = db.Set<TFile>()
        .AsNoTracking()
        .Where(f => distinctKeys.Contains(f.StorageKey) || f.ThumbnailKey != null && distinctKeys.Contains(f.ThumbnailKey))
        .Select(f => new { f.StorageKey, f.ThumbnailKey });
      var referencedPairs = referencedQuery is IAsyncEnumerable<object>
        ? await referencedQuery.ToListAsync(CancellationToken.None)
        : referencedQuery.ToList();

      var referencedKeys = referencedPairs
        .SelectMany(static f => new[] { f.StorageKey, f.ThumbnailKey })
        .Where(static k => !string.IsNullOrEmpty(k))
        .ToHashSet(StringComparer.Ordinal);

      foreach (var (itemStore, key) in keysToDelete.DistinctBy(static x => x.Key))
      {
        if (referencedKeys.Contains(key))
        {
          continue;
        }

        try
        {
          await stores.Of(itemStore).DeleteAsync(key, CancellationToken.None);
        }
        catch
        {
        }
      }
    }
    catch
    {
    }
  }

  public static Task RemoveDependentFilesAsync<TEntity, TFile>(
    GridDeleteContext<TEntity> context,
    Expression<Func<TFile, long>> foreignKey,
    List<Func<IServiceProvider?, CancellationToken, Task>> postCommitCleanups,
    CancellationToken ct)
    where TEntity : class, IEntity
    where TFile : BaseAppFile
  {
    if (context.Db is not PlatformDbContext platformDb || context.Records.IsEmpty)
    {
      return Task.CompletedTask;
    }

    var ids = context.Records.Select(static r => r.Id).Distinct().ToArray();
    var containsCall = Expression.Call(EnumerableContainsLongMethod, Expression.Constant(ids), foreignKey.Body);
    var predicate = Expression.Lambda<Func<TFile, bool>>(containsCall, foreignKey.Parameters[0]);
    return RemoveMatchingFilesCoreAsync(platformDb, predicate, postCommitCleanups, ct);
  }

  public static Task RemoveDependentFilesAsync<TEntity, TFile>(
    GridDeleteContext<TEntity> context,
    Expression<Func<TFile, long?>> foreignKey,
    List<Func<IServiceProvider?, CancellationToken, Task>> postCommitCleanups,
    CancellationToken ct)
    where TEntity : class, IEntity
    where TFile : BaseAppFile
  {
    if (context.Db is not PlatformDbContext platformDb || context.Records.IsEmpty)
    {
      return Task.CompletedTask;
    }

    var nullableIds = context.Records.Select(static r => (long?)r.Id).Distinct().ToArray();
    var containsCall = Expression.Call(EnumerableContainsNullableLongMethod, Expression.Constant(nullableIds), foreignKey.Body);
    var predicate = Expression.Lambda<Func<TFile, bool>>(containsCall, foreignKey.Parameters[0]);
    return RemoveMatchingFilesCoreAsync(platformDb, predicate, postCommitCleanups, ct);
  }

  private static async Task RemoveMatchingFilesCoreAsync<TFile>(
    PlatformDbContext db,
    Expression<Func<TFile, bool>> predicate,
    List<Func<IServiceProvider?, CancellationToken, Task>> postCommitCleanups,
    CancellationToken ct)
    where TFile : BaseAppFile
  {
    var query = db.Set<TFile>().AsTracking().Where(predicate);
    var files = query is IAsyncEnumerable<TFile>
      ? await query.ToListAsync(ct)
      : query.ToList();
    if (files.Count == 0)
    {
      return;
    }

    var keys = CollectKeys(files);
    db.Set<TFile>().RemoveRange(files);
    if (keys.Count > 0)
    {
      postCommitCleanups.Add((services, cleanupCt) =>
        DeleteUnreferencedBlobsAsync<TFile>(db, ResolveStores(services), keys, cleanupCt));
    }
  }
}
