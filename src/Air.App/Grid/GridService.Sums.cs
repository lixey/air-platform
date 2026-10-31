using Platform.App.Grid.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  private static async Task<SumRowBucket?> ExecuteSumBucketAsync<TSource>(
    IQueryable<TSource> source,
    List<(PropertyInfo BucketProp, LambdaExpression ValueSelector)> bindings,
    CancellationToken ct)
  {
    var gParam = Expression.Parameter(typeof(IGrouping<int, TSource>), "g");
    var memberBindings = new List<MemberBinding>(bindings.Count);
    foreach (var (bucketProp, valueSelector) in bindings)
    {
      var sumMethod = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(Enumerable.Sum) &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 2 &&
                    m.ReturnType == bucketProp.PropertyType)
        .MakeGenericMethod(typeof(TSource));
      memberBindings.Add(Expression.Bind(bucketProp, Expression.Call(sumMethod, gParam, valueSelector)));
    }

    var body = Expression.MemberInit(Expression.New(typeof(SumRowBucket)), memberBindings);
    var selectLambda = Expression.Lambda<Func<IGrouping<int, TSource>, SumRowBucket>>(body, gParam);
    return await source.GroupBy(_ => 1).Select(selectLambda).FirstOrDefaultAsync(ct);
  }

  private async Task<List<object>?> ComputeSumRowsAsync(
    IQueryable<TEntity> filteredEntities,
    Expression<Func<TEntity, TRow>>? selector,
    IReadOnlyList<GridColumnDto> columns,
    CancellationToken ct)
  {
    var sumCols = columns.Where(c => c.IsSummed && !string.IsNullOrEmpty(c.Path)).ToList();
    if (sumCols.Count == 0)
    {
      return null;
    }

    var sumRow = new Dictionary<string, object?>(StringComparer.Ordinal);
    var rowType = selector != null ? typeof(TRow) : typeof(TEntity);
    var rowParam = Expression.Parameter(rowType, "r");

    if (sumCols.All(c => c.SumPredicate == null) &&
        sumCols.Count <= SumRowBucket.DecimalProps.Length)
    {
      var convertDecimalToDouble = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
      var bindings = new List<(PropertyInfo BucketProp, LambdaExpression ValueSelector)>(sumCols.Count);
      var readers = new List<(GridColumnDto Col, Type Underlying, PropertyInfo BucketProp)>(sumCols.Count);
      int dIdx = 0, lIdx = 0, fIdx = 0;
      var canBatchAll = true;

      foreach (var col in sumCols)
      {
        Expression? current = rowParam;
        var currentType = rowType;
        foreach (var seg in col.Path!.Split('.'))
        {
          var prop = PropertyAccessorCache.GetProperty(currentType, seg);
          if (prop == null)
          {
            current = null;
            break;
          }

          current = Expression.Property(current, prop);
          currentType = prop.PropertyType;
        }

        if (current == null)
        {
          continue;
        }

        var underlying = Nullable.GetUnderlyingType(currentType) ?? currentType;
        if (underlying == typeof(decimal) && !convertDecimalToDouble && dIdx < SumRowBucket.DecimalProps.Length)
        {
          var body = currentType == typeof(decimal?) ? current : Expression.Convert(current, typeof(decimal?));
          var bp = SumRowBucket.DecimalProps[dIdx++];
          bindings.Add((bp, Expression.Lambda(body, rowParam)));
          readers.Add((col, typeof(decimal), bp));
        }
        else if (underlying == typeof(decimal) && convertDecimalToDouble && fIdx < SumRowBucket.DoubleProps.Length)
        {
          var body = Expression.Convert(current, typeof(double?));
          var bp = SumRowBucket.DoubleProps[fIdx++];
          bindings.Add((bp, Expression.Lambda(body, rowParam)));
          readers.Add((col, typeof(decimal), bp));
        }
        else if ((underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short)) && lIdx < SumRowBucket.LongProps.Length)
        {
          var body = currentType == typeof(long?) ? current : Expression.Convert(current, typeof(long?));
          var bp = SumRowBucket.LongProps[lIdx++];
          bindings.Add((bp, Expression.Lambda(body, rowParam)));
          readers.Add((col, typeof(long), bp));
        }
        else if ((underlying == typeof(double) || underlying == typeof(float)) && fIdx < SumRowBucket.DoubleProps.Length)
        {
          var body = currentType == typeof(double?) ? current : Expression.Convert(current, typeof(double?));
          var bp = SumRowBucket.DoubleProps[fIdx++];
          bindings.Add((bp, Expression.Lambda(body, rowParam)));
          readers.Add((col, typeof(double), bp));
        }
        else
        {
          canBatchAll = false;
          break;
        }
      }

      if (canBatchAll && bindings.Count > 0)
      {
        var bucket = selector != null
          ? await ExecuteSumBucketAsync(filteredEntities.Select(selector), bindings, ct)
          : await ExecuteSumBucketAsync(filteredEntities, bindings, ct);

        foreach (var (col, underlying, bp) in readers)
        {
          var rawVal = bucket != null ? bp.GetValue(bucket) : null;
          if (underlying == typeof(decimal))
          {
            var total = rawVal switch
            {
              decimal d => d,
              double dbl => Convert.ToDecimal(dbl),
              _ => 0m
            };
            PutValue(sumRow, col.Path!, total);
            if (col.SumColorEvaluator != null)
            {
              var sumColor = col.SumColorEvaluator(total, false);
              if (!string.IsNullOrEmpty(sumColor))
              {
                sumRow[$"{col.Path}Color"] = sumColor;
              }
            }
          }
          else if (underlying == typeof(long))
          {
            var total = (rawVal as long?) ?? 0L;
            PutValue(sumRow, col.Path!, total);
            if (col.SumColorEvaluator != null)
            {
              var sumColor = col.SumColorEvaluator(total, false);
              if (!string.IsNullOrEmpty(sumColor))
              {
                sumRow[$"{col.Path}Color"] = sumColor;
              }
            }
          }
          else if (underlying == typeof(double))
          {
            var total = (rawVal as double?) ?? 0d;
            PutValue(sumRow, col.Path!, total);
          }
        }

        return sumRow.Count > 0 ? [sumRow] : null;
      }
    }

    foreach (var col in sumCols)
    {
      if (string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

      Expression? current = rowParam;
      var currentType = rowType;
      foreach (var seg in col.Path.Split('.'))
      {
        var prop = PropertyAccessorCache.GetProperty(currentType, seg);
        if (prop == null)
        {
          current = null;
          break;
        }

        current = Expression.Property(current, prop);
        currentType = prop.PropertyType;
      }

      if (current == null)
      {
        continue;
      }

      var underlying = Nullable.GetUnderlyingType(currentType) ?? currentType;
      var entityPred = col.SumPredicate as Expression<Func<TEntity, bool>>;

      try
      {
        if (underlying == typeof(decimal))
        {
          var body = currentType == typeof(decimal?) ? current : Expression.Convert(current, typeof(decimal?));
          decimal total;
          var usedFallback = false;

          if (selector != null)
          {
            var expr = Expression.Lambda<Func<TRow, decimal?>>(body, rowParam);
            if (entityPred != null)
            {
              var predSum = await filteredEntities.Where(entityPred).Select(selector).SumAsync(expr, ct);
              if (predSum.HasValue)
              {
                total = predSum.Value;
              }
              else
              {
                var fallbackSum = await filteredEntities.Select(selector).SumAsync(expr, ct);
                total = fallbackSum ?? 0m;
                usedFallback = fallbackSum.HasValue;
              }
            }
            else
            {
              total = await filteredEntities.Select(selector).SumAsync(expr, ct) ?? 0m;
            }
          }
          else
          {
            var expr = Expression.Lambda<Func<TEntity, decimal?>>(body, rowParam);
            if (entityPred != null)
            {
              var predSum = await filteredEntities.Where(entityPred).SumAsync(expr, ct);
              if (predSum.HasValue)
              {
                total = predSum.Value;
              }
              else
              {
                var fallbackSum = await filteredEntities.SumAsync(expr, ct);
                total = fallbackSum ?? 0m;
                usedFallback = fallbackSum.HasValue;
              }
            }
            else
            {
              total = await filteredEntities.SumAsync(expr, ct) ?? 0m;
            }
          }

          PutValue(sumRow, col.Path, total);
          if (col.SumColorEvaluator != null)
          {
            var sumColor = col.SumColorEvaluator(total, usedFallback);
            if (!string.IsNullOrEmpty(sumColor))
            {
              sumRow[$"{col.Path}Color"] = sumColor;
            }
          }
        }
        else if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
        {
          var body = currentType == typeof(long?) ? current : Expression.Convert(current, typeof(long?));
          var sourceEntities = entityPred != null ? filteredEntities.Where(entityPred) : filteredEntities;
          var total = selector != null
            ? await sourceEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, long?>>(body, rowParam), ct) ?? 0L
            : await sourceEntities.SumAsync(Expression.Lambda<Func<TEntity, long?>>(body, rowParam), ct) ?? 0L;
          PutValue(sumRow, col.Path, total);
          if (col.SumColorEvaluator != null)
          {
            var sumColor = col.SumColorEvaluator(total, false);
            if (!string.IsNullOrEmpty(sumColor))
            {
              sumRow[$"{col.Path}Color"] = sumColor;
            }
          }
        }
        else if (underlying == typeof(double) || underlying == typeof(float))
        {
          var body = currentType == typeof(double?) ? current : Expression.Convert(current, typeof(double?));
          var sourceEntities = entityPred != null ? filteredEntities.Where(entityPred) : filteredEntities;
          var total = selector != null
            ? await sourceEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, double?>>(body, rowParam), ct) ?? 0d
            : await sourceEntities.SumAsync(Expression.Lambda<Func<TEntity, double?>>(body, rowParam), ct) ?? 0d;
          PutValue(sumRow, col.Path, total);
        }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch { }
    }

    return sumRow.Count > 0 ? [sumRow] : null;
  }
}
