using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  private async Task<List<object>?> ComputeSumRowsAsync(
    IQueryable<TEntity> filteredEntities,
    Expression<Func<TEntity, TRow>>? selector,
    IReadOnlyList<GridColumnDto> columns,
    CancellationToken ct)
  {
    var sumCols = columns.Where(c => c.IsSummed && !c.IsComputed && !string.IsNullOrEmpty(c.Path)).ToList();
    if (sumCols.Count == 0)
    {
      return null;
    }

    var sumRow = new Dictionary<string, object?>(StringComparer.Ordinal);
    var rowType = selector != null ? typeof(TRow) : typeof(TEntity);
    var rowParam = Expression.Parameter(rowType, "r");
    var convertDecimalToDouble = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

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

      try
      {
        if (underlying == typeof(decimal))
        {
          decimal total;
          if (convertDecimalToDouble)
          {
            var dblBody = Expression.Convert(current, typeof(double?));
            var dblSum = selector != null
              ? await filteredEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, double?>>(dblBody, rowParam), ct)
              : await filteredEntities.SumAsync(Expression.Lambda<Func<TEntity, double?>>(dblBody, rowParam), ct);
            total = dblSum.HasValue ? Convert.ToDecimal(dblSum.Value) : 0m;
          }
          else
          {
            var body = currentType == typeof(decimal?) ? current : Expression.Convert(current, typeof(decimal?));
            total = selector != null
              ? await filteredEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, decimal?>>(body, rowParam), ct) ?? 0m
              : await filteredEntities.SumAsync(Expression.Lambda<Func<TEntity, decimal?>>(body, rowParam), ct) ?? 0m;
          }

          PutValue(sumRow, col.Path, total);
        }
        else if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
        {
          var body = currentType == typeof(long?) ? current : Expression.Convert(current, typeof(long?));
          var total = selector != null
            ? await filteredEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, long?>>(body, rowParam), ct) ?? 0L
            : await filteredEntities.SumAsync(Expression.Lambda<Func<TEntity, long?>>(body, rowParam), ct) ?? 0L;
          PutValue(sumRow, col.Path, total);
        }
        else if (underlying == typeof(double) || underlying == typeof(float))
        {
          var body = currentType == typeof(double?) ? current : Expression.Convert(current, typeof(double?));
          var total = selector != null
            ? await filteredEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, double?>>(body, rowParam), ct) ?? 0d
            : await filteredEntities.SumAsync(Expression.Lambda<Func<TEntity, double?>>(body, rowParam), ct) ?? 0d;
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
