using Platform.Data.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridNamedFilterCounter
{
  private static readonly MethodInfo CountUnconditionalDef = typeof(Enumerable)
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Single(static m => m.Name == nameof(Enumerable.Count) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);

  private static readonly MethodInfo CountConditionalDef = typeof(Enumerable)
    .GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Single(static m => m.Name == nameof(Enumerable.Count) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);

  public static async Task<Dictionary<string, int>?> ComputeAsync<TEntity, TRow>(
    IQueryable<TEntity> baseEntityQuery,
    GridRequest? gridReq,
    ListRequest request,
    GridQuery<TEntity, TRow> queryDeclaration,
    QueryMap<TEntity> queryMap,
    int? currentTotal,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var hasCountedGroup = queryDeclaration.NamedFilterGroups.Any(static g => g.Items.Any(static i => i.IsCountShown));
    var hasCountedToggle = queryDeclaration.NamedFilters.Any(static i => i.IsCountShown);
    if (!hasCountedGroup && !hasCountedToggle)
    {
      return null;
    }

    var nfs = gridReq?.NamedFilters ?? [];
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    var chosenByGroup = new List<(GridNamedFilterGroup<TEntity> Group, GridNamedFilterItem<TEntity>? Chosen)>(queryDeclaration.NamedFilterGroups.Count);
    foreach (var group in queryDeclaration.NamedFilterGroups)
    {
      var reqKey = nfs.FirstOrDefault(k => group.Items.Any(i => string.Equals(i.Key, k, StringComparison.OrdinalIgnoreCase)));
      var chosen = reqKey != null
        ? group.Items.FirstOrDefault(i => string.Equals(i.Key, reqKey, StringComparison.OrdinalIgnoreCase))
        : group.Items.FirstOrDefault(static i => i.IsDefault);
      chosenByGroup.Add((group, chosen));
    }

    var activeToggles = queryDeclaration.NamedFilters
      .Where(nf => nfs.Any(k => string.Equals(k, nf.Key, StringComparison.OrdinalIgnoreCase)))
      .ToList();

    var pendingSlots = new List<(string Key, Expression<Func<TEntity, bool>>? Predicate)>();

    foreach (var (group, chosen) in chosenByGroup)
    {
      foreach (var item in group.Items)
      {
        if (!item.IsCountShown)
        {
          continue;
        }

        if (ReferenceEquals(item, chosen) && currentTotal.HasValue)
        {
          counts[item.Key] = currentTotal.Value;
          continue;
        }

        var predicates = new List<Expression<Func<TEntity, bool>>>();
        foreach (var (otherGroup, otherChosen) in chosenByGroup)
        {
          var activeItem = ReferenceEquals(otherGroup, group) ? item : otherChosen;
          if (activeItem?.Predicate != null)
          {
            predicates.Add(activeItem.Predicate);
          }
        }

        foreach (var activeToggle in activeToggles)
        {
          if (activeToggle.Predicate != null)
          {
            predicates.Add(activeToggle.Predicate);
          }
        }

        pendingSlots.Add((item.Key, CombinePredicates(predicates)));
      }
    }

    foreach (var nf in queryDeclaration.NamedFilters)
    {
      if (!nf.IsCountShown)
      {
        continue;
      }

      var isCurrentlyOn = activeToggles.Contains(nf);
      if (isCurrentlyOn && currentTotal.HasValue)
      {
        counts[nf.Key] = currentTotal.Value;
        continue;
      }

      var predicates = new List<Expression<Func<TEntity, bool>>>();
      foreach (var (_, chosen) in chosenByGroup)
      {
        if (chosen?.Predicate != null)
        {
          predicates.Add(chosen.Predicate);
        }
      }

      if (nf.Predicate != null)
      {
        predicates.Add(nf.Predicate);
      }

      foreach (var otherToggle in activeToggles)
      {
        if (!ReferenceEquals(otherToggle, nf) && otherToggle.Predicate != null)
        {
          predicates.Add(otherToggle.Predicate);
        }
      }

      pendingSlots.Add((nf.Key, CombinePredicates(predicates)));
    }

    if (pendingSlots.Count == 0)
    {
      return counts.Count > 0 ? counts : null;
    }

    var userFilteredBase = DataProvider.Create(baseEntityQuery.AsNoTracking(), queryMap).Filter(request);
    var batchSize = GridCountBucket.IntProps.Length;

    for (var offset = 0; offset < pendingSlots.Count; offset += batchSize)
    {
      var count = Math.Min(batchSize, pendingSlots.Count - offset);
      var slice = pendingSlots.GetRange(offset, count);
      if (slice.Count == 1)
      {
        var single = slice[0];
        counts[single.Key] = await CountSingleAsync(userFilteredBase, single.Predicate, ct);
        continue;
      }

      if (!await TryCountBatchAsync(userFilteredBase, slice, counts, ct))
      {
        foreach (var (key, predicate) in slice)
        {
          counts[key] = await CountSingleAsync(userFilteredBase, predicate, ct);
        }
      }
    }

    return counts.Count > 0 ? counts : null;
  }

  private static async Task<bool> TryCountBatchAsync<TEntity>(
    IQueryable<TEntity> source,
    List<(string Key, Expression<Func<TEntity, bool>>? Predicate)> slice,
    Dictionary<string, int> counts,
    CancellationToken ct)
    where TEntity : class
  {
    if (source is not IAsyncEnumerable<TEntity>)
    {
      return false;
    }

    try
    {
      var groupParam = Expression.Parameter(typeof(IGrouping<int, TEntity>), "g");
      var countAllMethod = CountUnconditionalDef.MakeGenericMethod(typeof(TEntity));
      var countWhereMethod = CountConditionalDef.MakeGenericMethod(typeof(TEntity));
      var bindings = new List<MemberBinding>(slice.Count);

      for (var i = 0; i < slice.Count; i++)
      {
        var predicate = slice[i].Predicate;
        var countCall = predicate == null
          ? Expression.Call(countAllMethod, groupParam)
          : Expression.Call(countWhereMethod, groupParam, predicate);
        bindings.Add(Expression.Bind(GridCountBucket.IntProps[i], countCall));
      }

      var selector = Expression.Lambda<Func<IGrouping<int, TEntity>, GridCountBucket>>(
        Expression.MemberInit(Expression.New(typeof(GridCountBucket)), bindings),
        groupParam);

      var bucket = await source
        .GroupBy(_ => 1)
        .Select(selector)
        .FirstOrDefaultAsync(ct);

      for (var i = 0; i < slice.Count; i++)
      {
        counts[slice[i].Key] = bucket != null ? GridCountBucket.Readers[i](bucket) : 0;
      }

      return true;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch
    {
      return false;
    }
  }

  private static Task<int> CountSingleAsync<TEntity>(
    IQueryable<TEntity> source,
    Expression<Func<TEntity, bool>>? predicate,
    CancellationToken ct)
  {
    var query = predicate != null ? source.Where(predicate) : source;
    return query is IAsyncEnumerable<TEntity>
      ? query.CountAsync(ct)
      : Task.FromResult(query.Count());
  }

  private static Expression<Func<TEntity, bool>>? CombinePredicates<TEntity>(
    List<Expression<Func<TEntity, bool>>> predicates)
  {
    if (predicates.Count == 0)
    {
      return null;
    }

    if (predicates.Count == 1)
    {
      return predicates[0];
    }

    var entityParam = Expression.Parameter(typeof(TEntity), "e");
    var combined = ParameterRebinder.Rebind(predicates[0], entityParam);
    for (var i = 1; i < predicates.Count; i++)
    {
      combined = Expression.AndAlso(combined, ParameterRebinder.Rebind(predicates[i], entityParam));
    }

    return Expression.Lambda<Func<TEntity, bool>>(combined, entityParam);
  }
}
