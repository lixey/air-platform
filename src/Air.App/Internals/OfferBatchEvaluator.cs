using Platform.Data.Internals;

namespace Platform.App.Internals;

internal static class OfferBatchEvaluator
{
  private static readonly PropertyInfo IdProperty = typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.Id))!;

  private static readonly PropertyInfo[] FlagProperties =
  [
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B0))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B1))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B2))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B3))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B4))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B5))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B6))!,
    typeof(OfferBatchRow).GetProperty(nameof(OfferBatchRow.B7))!
  ];

  public static async Task<Dictionary<long, Dictionary<string, int>>> EvaluateAsync<TEntity>(
    PlatformDbContext db,
    IReadOnlyList<long> rowIds,
    IReadOnlyList<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)> offers,
    CancellationToken ct = default)
    where TEntity : class, IEntity
  {
    var result = new Dictionary<long, Dictionary<string, int>>();
    if (rowIds.Count == 0 || offers.Count == 0)
    {
      return result;
    }

    var entityIdProp = typeof(TEntity).GetProperty(nameof(IEntity.Id))!;
    for (var offset = 0; offset < offers.Count; offset += FlagProperties.Length)
    {
      var count = Math.Min(FlagProperties.Length, offers.Count - offset);
      var chunk = offers.Skip(offset).Take(count).ToList();
      try
      {
        var param = Expression.Parameter(typeof(TEntity), "e");
        var bindings = new List<MemberBinding>(count + 1)
        {
          Expression.Bind(IdProperty, Expression.Property(param, entityIdProp))
        };

        for (var i = 0; i < count; i++)
        {
          var cond = chunk[i].Condition;
          var replaced = ParameterRebinder.Rebind(cond, param);
          bindings.Add(Expression.Bind(FlagProperties[i], replaced));
        }

        var selector = Expression.Lambda<Func<TEntity, OfferBatchRow>>(
          Expression.MemberInit(Expression.New(typeof(OfferBatchRow)), bindings),
          param);

        var rows = await db.Set<TEntity>()
          .AsNoTracking()
          .Where(e => rowIds.Contains(e.Id))
          .Select(selector)
          .ToListAsync(ct);

        var rowsById = rows.ToDictionary(r => r.Id);
        foreach (var rowId in rowIds)
        {
          rowsById.TryGetValue(rowId, out var row);
          for (var i = 0; i < count; i++)
          {
            var passed = row != null && row.GetFlag(i);
            if (!passed)
            {
              if (!result.TryGetValue(rowId, out var unav))
              {
                unav = new Dictionary<string, int>(StringComparer.Ordinal);
                result[rowId] = unav;
              }

              unav.TryAdd(chunk[i].ActionKey, chunk[i].ReasonIndex);
            }
          }
        }
      }
      catch (Exception ex) when (ex is not OperationCanceledException && db.Database.CurrentTransaction == null)
      {
        foreach (var (actionKey, reasonIndex, cond) in chunk)
        {
          var passedIds = (await db.Set<TEntity>()
            .AsNoTracking()
            .Where(e => rowIds.Contains(e.Id))
            .Where(cond)
            .Select(e => e.Id)
            .ToListAsync(ct)).ToHashSet();

          foreach (var rowId in rowIds)
          {
            if (!passedIds.Contains(rowId))
            {
              if (!result.TryGetValue(rowId, out var unav))
              {
                unav = new Dictionary<string, int>(StringComparer.Ordinal);
                result[rowId] = unav;
              }

              unav.TryAdd(actionKey, reasonIndex);
            }
          }
        }
      }
    }

    return result;
  }
}
