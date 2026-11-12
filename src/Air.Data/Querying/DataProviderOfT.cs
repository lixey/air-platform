using Platform.Data.Internals;

namespace Platform.Data.Querying;

public sealed class DataProvider<TEntity, TRow>
  where TEntity : class
  where TRow : class
{
  private static readonly MethodInfo EnumerableContains = typeof(Enumerable)
    .GetMethods()
    .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

  private static readonly MethodInfo StreamChunksByKeyMethod = typeof(DataProvider<TEntity, TRow>)
    .GetMethod(nameof(StreamChunksByKeyAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

  private readonly Expression<Func<TEntity, TRow>>? _selector;

  public DataProvider(IQueryable<TEntity> source, QueryMap<TEntity> map, Expression<Func<TEntity, TRow>>? selector = null)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(map);
    Source = source;
    _selector = selector;
    Map = map;
  }

  public IQueryable<TEntity> Source { get; }

  public QueryMap<TEntity> Map { get; }

  public IQueryable<TEntity> Filter(ListRequest request) => Map.Plan(request).Filter(Source);

  public Task<int> CountAsync(ListRequest request, CancellationToken cancellationToken = default) =>
    ExecuteCountAsync(Filter(request), cancellationToken);

  public async Task<Page<TRow>> GetPageAsync(ListRequest request, CancellationToken cancellationToken = default)
  {
    var plan = Map.Plan(request);
    var filtered = plan.Filter(Source);
    var ordered = plan.Order(filtered);
    var size = plan.PageSize;

    var skip = (long)(plan.Page - 1) * size;
    var rows = skip <= int.MaxValue
      ? await ExecuteListAsync(Project(ordered.Skip((int)skip).Take(size + 1)), cancellationToken)
      : [];

    if (rows.Count > 0 || plan.Page == 1)
    {
      var hasMore = rows.Count > size;
      if (hasMore)
      {
        rows.RemoveAt(size);
      }

      return new Page<TRow>
      {
        Rows = rows,
        Total = hasMore ? null : (int)skip + rows.Count,
        Number = plan.Page,
        Size = size,
        HasMore = hasMore
      };
    }

    var total = await ExecuteCountAsync(filtered, cancellationToken);
    var last = Math.Max(1, (total + size - 1) / size);
    return new Page<TRow>
    {
      Rows = await ExecuteListAsync(Project(ordered.Skip((last - 1) * size).Take(size)), cancellationToken),
      Total = total,
      Number = last,
      Size = size,
      HasMore = false
    };
  }

  public async Task<Slice<TRow>> GetSliceAsync(ListRequest request, int limit = 1000, CancellationToken cancellationToken = default)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
    var plan = Map.Plan(request);

    var rows = await ExecuteListAsync(Project(plan.Order(plan.Filter(Source)).Take(limit + 1)), cancellationToken);
    var isTruncated = rows.Count > limit;
    if (isTruncated)
    {
      rows.RemoveAt(limit);
    }

    return new Slice<TRow> { Rows = rows, IsTruncated = isTruncated };
  }

  public Task<TRow?> GetOneAsync(object key, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(key);
    var entity = Expression.Parameter(typeof(TEntity), "e");
    var member = Map.Key.Bind(entity);
    var value = RequestValue.Convert(key, member.Type, Map.Key.Name);
    var matches = Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(member, BoundValue.Of(value, member.Type)), entity);
    return ExecuteFirstOrDefaultAsync(Project(Source.Where(matches)), cancellationToken);
  }

  public IAsyncEnumerable<TRow> StreamAsync(ListRequest request, int limit, CancellationToken cancellationToken = default)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
    var plan = Map.Plan(request);
    return EnumerateAsync(Project(plan.Order(plan.Filter(Source)).Take(limit)), cancellationToken);
  }

  public IAsyncEnumerable<IReadOnlyList<TRow>> StreamChunksAsync(
    ListRequest request,
    int limit,
    int chunkSize = 500,
    CancellationToken cancellationToken = default)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

    var plan = Map.Plan(request);
    var ordered = plan.Order(plan.Filter(Source));

    if (limit <= chunkSize)
    {
      return StreamSingleChunkAsync(ordered, limit, cancellationToken);
    }

    return Map.Key.Expression switch
    {
      Expression<Func<TEntity, long>> longKey => StreamChunksByKeyAsync(plan, ordered, longKey, limit, chunkSize, cancellationToken),
      Expression<Func<TEntity, int>> intKey => StreamChunksByKeyAsync(plan, ordered, intKey, limit, chunkSize, cancellationToken),
      Expression<Func<TEntity, Guid>> guidKey => StreamChunksByKeyAsync(plan, ordered, guidKey, limit, chunkSize, cancellationToken),
      Expression<Func<TEntity, string>> stringKey => StreamChunksByKeyAsync(plan, ordered, stringKey, limit, chunkSize, cancellationToken),
      var keyExpr => (IAsyncEnumerable<IReadOnlyList<TRow>>)StreamChunksByKeyMethod
        .MakeGenericMethod(keyExpr.ReturnType)
        .Invoke(this, [plan, ordered, keyExpr, limit, chunkSize, cancellationToken])!
    };
  }

  private async IAsyncEnumerable<IReadOnlyList<TRow>> StreamSingleChunkAsync(
    IQueryable<TEntity> ordered,
    int limit,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var rows = await ExecuteListAsync(Project(ordered.Take(limit)), cancellationToken);
    if (rows.Count > 0)
    {
      yield return rows;
    }
  }

  private async IAsyncEnumerable<IReadOnlyList<TRow>> StreamChunksByKeyAsync<TKey>(
    QueryPlan<TEntity> plan,
    IQueryable<TEntity> ordered,
    Expression<Func<TEntity, TKey>> keySelector,
    int limit,
    int chunkSize,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var keys = await ExecuteListAsync(ordered.Select(keySelector).Take(limit), cancellationToken);
    if (keys.Count == 0)
    {
      yield break;
    }

    var containsMethod = EnumerableContains.MakeGenericMethod(typeof(TKey));
    for (var offset = 0; offset < keys.Count; offset += chunkSize)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var count = Math.Min(chunkSize, keys.Count - offset);
      var batchKeys = keys.GetRange(offset, count);

      var entity = Expression.Parameter(typeof(TEntity), "e");
      var keyMember = Map.Key.Bind(entity);
      var inCall = Expression.Call(
        containsMethod,
        BoundValue.Of(batchKeys, typeof(List<TKey>)),
        keyMember);
      var predicate = Expression.Lambda<Func<TEntity, bool>>(inCall, entity);

      var chunk = await ExecuteListAsync(Project(plan.Order(Source.Where(predicate))), cancellationToken);
      if (chunk.Count > 0)
      {
        yield return chunk;
      }
    }
  }

  private IQueryable<TRow> Project(IQueryable<TEntity> query) =>
    _selector is not null ? query.Select(_selector) : (IQueryable<TRow>)query;

  private static Task<List<T>> ExecuteListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
    query is IAsyncEnumerable<T>
      ? query.ToListAsync(cancellationToken)
      : Task.FromResult(query.ToList());

  private static Task<int> ExecuteCountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
    query is IAsyncEnumerable<T>
      ? query.CountAsync(cancellationToken)
      : Task.FromResult(query.Count());

  private static Task<T?> ExecuteFirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken) =>
    query is IAsyncEnumerable<T>
      ? query.FirstOrDefaultAsync(cancellationToken)
      : Task.FromResult(query.FirstOrDefault());

  private static async IAsyncEnumerable<TRow> EnumerateAsync(
    IQueryable<TRow> query,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    if (query is IAsyncEnumerable<TRow> rows)
    {
      await foreach (var row in rows.WithCancellation(cancellationToken))
      {
        yield return row;
      }

      yield break;
    }

    foreach (var item in query)
    {
      cancellationToken.ThrowIfCancellationRequested();
      yield return item;
    }
  }
}
