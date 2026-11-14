using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridSummaryCalculator
{
  public static async Task<List<RowDto>?> ComputeSumRowsAsync<TEntity, TRow>(
    PlatformDbContext db,
    IQueryable<TEntity> filteredEntities,
    Expression<Func<TEntity, TRow>>? selector,
    IReadOnlyList<GridColumnDto> columns,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var sumCols = columns
      .Where(c => !string.IsNullOrEmpty(c.Path) && GridColumnRuntime.TryGet(c) is { IsSummed: true, IsComputed: false })
      .ToList();
    if (sumCols.Count == 0)
    {
      return null;
    }

    var sumRow = new RowDto();
    var rowType = selector != null ? typeof(TRow) : typeof(TEntity);
    var rowParam = Expression.Parameter(rowType, "r");
    var convertDecimalToDouble = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    var resolved = new List<(GridColumnDto Col, Expression Expr, Type ValueType, Type UnderlyingType)>(sumCols.Count);
    foreach (var col in sumCols)
    {
      if (string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

      var runtime = GridColumnRuntime.TryGet(col);
      Expression? current;
      Type? currentType;

      if (selector == null && runtime?.ValueExpression != null)
      {
        current = ParameterRebinder.Rebind(runtime.ValueExpression, rowParam);
        currentType = runtime.ValueExpression.ReturnType;
      }
      else
      {
        current = rowParam;
        currentType = rowType;
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
      }

      if (current == null || currentType == null)
      {
        continue;
      }

      var underlying = Nullable.GetUnderlyingType(currentType) ?? currentType;
      if (underlying == typeof(decimal) ||
          underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) ||
          underlying == typeof(double) || underlying == typeof(float))
      {
        resolved.Add((col, current, currentType, underlying));
      }
    }

    if (resolved.Count == 0)
    {
      return null;
    }

    if (!convertDecimalToDouble &&
        resolved.Count > 1 &&
        resolved.All(r => GridColumnRuntime.TryGet(r.Col)?.IsSubqueryExpression != true) &&
        await TryComputeBatchSumsAsync<TEntity, TRow>(filteredEntities, selector, rowParam, resolved, sumRow, ct))
    {
      return sumRow.Cells.Count > 0 ? [sumRow] : null;
    }

    foreach (var (col, current, currentType, underlying) in resolved)
    {
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

          PutSumValue<TEntity>(sumRow, col, total);
        }
        else if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
        {
          var body = currentType == typeof(long?) ? current : Expression.Convert(current, typeof(long?));
          var total = selector != null
            ? await filteredEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, long?>>(body, rowParam), ct) ?? 0L
            : await filteredEntities.SumAsync(Expression.Lambda<Func<TEntity, long?>>(body, rowParam), ct) ?? 0L;
          PutSumValue<TEntity>(sumRow, col, total);
        }
        else if (underlying == typeof(double) || underlying == typeof(float))
        {
          var body = currentType == typeof(double?) ? current : Expression.Convert(current, typeof(double?));
          var total = selector != null
            ? await filteredEntities.Select(selector).SumAsync(Expression.Lambda<Func<TRow, double?>>(body, rowParam), ct) ?? 0d
            : await filteredEntities.SumAsync(Expression.Lambda<Func<TEntity, double?>>(body, rowParam), ct) ?? 0d;
          PutSumValue<TEntity>(sumRow, col, total);
        }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch { }
    }

    return sumRow.Cells.Count > 0 ? [sumRow] : null;
  }

  private static async Task<bool> TryComputeBatchSumsAsync<TEntity, TRow>(
    IQueryable<TEntity> filteredEntities,
    Expression<Func<TEntity, TRow>>? selector,
    ParameterExpression rowParam,
    List<(GridColumnDto Col, Expression Expr, Type ValueType, Type UnderlyingType)> resolved,
    RowDto sumRow,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    try
    {
      if (selector != null)
      {
        return await TryComputeBatchSumsCoreAsync<TEntity, TRow>(filteredEntities.Select(selector), rowParam, resolved, sumRow, ct);
      }

      return await TryComputeBatchSumsCoreAsync<TEntity, TEntity>(filteredEntities, rowParam, resolved, sumRow, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch
    {
      sumRow.Cells.Clear();
      sumRow.Colors = null;
      return false;
    }
  }

  private static async Task<bool> TryComputeBatchSumsCoreAsync<TEntity, TSource>(
    IQueryable<TSource> sourceQuery,
    ParameterExpression rowParam,
    List<(GridColumnDto Col, Expression Expr, Type ValueType, Type UnderlyingType)> resolved,
    RowDto sumRow,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
  {
    var dCount = 0;
    var lCount = 0;
    var fCount = 0;
    var groupParam = Expression.Parameter(typeof(IGrouping<int, TSource>), "g");
    var bindings = new List<MemberBinding>(resolved.Count);
    var readers = new List<(GridColumnDto Col, Func<GridSumBucket?, object> Reader)>(resolved.Count);

    var decSumMethod = typeof(Enumerable)
      .GetMethods(BindingFlags.Public | BindingFlags.Static)
      .First(m => m.Name == nameof(Enumerable.Sum) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 && m.ReturnType == typeof(decimal?))
      .MakeGenericMethod(typeof(TSource));
    var longSumMethod = typeof(Enumerable)
      .GetMethods(BindingFlags.Public | BindingFlags.Static)
      .First(m => m.Name == nameof(Enumerable.Sum) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 && m.ReturnType == typeof(long?))
      .MakeGenericMethod(typeof(TSource));
    var dblSumMethod = typeof(Enumerable)
      .GetMethods(BindingFlags.Public | BindingFlags.Static)
      .First(m => m.Name == nameof(Enumerable.Sum) && m.IsGenericMethodDefinition && m.GetParameters().Length == 2 && m.ReturnType == typeof(double?))
      .MakeGenericMethod(typeof(TSource));

    foreach (var (col, expr, valueType, underlying) in resolved)
    {
      if (underlying == typeof(decimal))
      {
        if (dCount >= GridSumBucket.DecimalProps.Length)
        {
          return false;
        }

        var idx = dCount++;
        var prop = GridSumBucket.DecimalProps[idx];
        var body = valueType == typeof(decimal?) ? expr : Expression.Convert(expr, typeof(decimal?));
        var sumCall = Expression.Call(decSumMethod, groupParam, Expression.Lambda<Func<TSource, decimal?>>(body, rowParam));
        bindings.Add(Expression.Bind(prop, sumCall));
        readers.Add((col, b => b != null ? (decimal?)prop.GetValue(b) ?? 0m : 0m));
      }
      else if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
      {
        if (lCount >= GridSumBucket.LongProps.Length)
        {
          return false;
        }

        var idx = lCount++;
        var prop = GridSumBucket.LongProps[idx];
        var body = valueType == typeof(long?) ? expr : Expression.Convert(expr, typeof(long?));
        var sumCall = Expression.Call(longSumMethod, groupParam, Expression.Lambda<Func<TSource, long?>>(body, rowParam));
        bindings.Add(Expression.Bind(prop, sumCall));
        readers.Add((col, b => b != null ? (long?)prop.GetValue(b) ?? 0L : 0L));
      }
      else if (underlying == typeof(double) || underlying == typeof(float))
      {
        if (fCount >= GridSumBucket.DoubleProps.Length)
        {
          return false;
        }

        var idx = fCount++;
        var prop = GridSumBucket.DoubleProps[idx];
        var body = valueType == typeof(double?) ? expr : Expression.Convert(expr, typeof(double?));
        var sumCall = Expression.Call(dblSumMethod, groupParam, Expression.Lambda<Func<TSource, double?>>(body, rowParam));
        bindings.Add(Expression.Bind(prop, sumCall));
        readers.Add((col, b => b != null ? (double?)prop.GetValue(b) ?? 0d : 0d));
      }
    }

    var bucketSelector = Expression.Lambda<Func<IGrouping<int, TSource>, GridSumBucket>>(
      Expression.MemberInit(Expression.New(typeof(GridSumBucket)), bindings),
      groupParam);

    var bucket = await sourceQuery
      .GroupBy(_ => 1)
      .Select(bucketSelector)
      .FirstOrDefaultAsync(ct);

    foreach (var (col, reader) in readers)
    {
      PutSumValue<TEntity>(sumRow, col, reader(bucket));
    }

    return true;
  }

  private static void PutSumValue<TEntity>(RowDto sumRow, GridColumnDto col, object total)
    where TEntity : class, IEntity, new()
  {
    if (string.IsNullOrEmpty(col.Path))
    {
      return;
    }

    GridRowShaper.PutValue(sumRow.Cells, col.Path, total);

    var runtime = GridColumnRuntime.TryGet(col);
    if (runtime?.ColorEvaluator == null)
    {
      return;
    }

    string? hue = null;
    try
    {
      if (runtime.ColorExpression == null)
      {
        hue = runtime.ColorEvaluator(total);
      }
      else if (ReferencesOnlyColumnProperty(runtime.ColorExpression, col.Path))
      {
        var prop = PropertyAccessorCache.GetProperty(typeof(TEntity), col.Path);
        if (prop is { CanWrite: true })
        {
          var dummy = new TEntity();
          var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
          prop.SetValue(dummy, Convert.ChangeType(total, targetType, CultureInfo.InvariantCulture));
          hue = runtime.ColorEvaluator(dummy);
        }
      }
    }
    catch { }

    if (!string.IsNullOrEmpty(hue))
    {
      (sumRow.Colors ??= new Dictionary<string, string>(StringComparer.Ordinal))[col.Path] = hue.ToLowerInvariant();
    }
  }

  private static bool ReferencesOnlyColumnProperty(LambdaExpression colorExpr, string colPath)
  {
    if (colorExpr.Parameters.Count != 1 || colPath.Contains('.'))
    {
      return false;
    }

    var param = colorExpr.Parameters[0];
    var foundProp = false;
    return CheckExpressionMembers(colorExpr.Body, param, colPath, ref foundProp) && foundProp;
  }

  private static bool CheckExpressionMembers(Expression? expr, ParameterExpression param, string colPath, ref bool foundProp)
  {
    switch (expr)
    {
      case null or ConstantExpression or ParameterExpression:
        return true;

      case MemberExpression me when me.Expression == param:
        if (!string.Equals(me.Member.Name, colPath, StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }

        foundProp = true;
        return true;

      case MemberExpression me:
        return CheckExpressionMembers(me.Expression, param, colPath, ref foundProp);

      case UnaryExpression ue:
        return CheckExpressionMembers(ue.Operand, param, colPath, ref foundProp);

      case BinaryExpression be:
        return CheckExpressionMembers(be.Left, param, colPath, ref foundProp) &&
               CheckExpressionMembers(be.Right, param, colPath, ref foundProp);

      case ConditionalExpression ce:
        return CheckExpressionMembers(ce.Test, param, colPath, ref foundProp) &&
               CheckExpressionMembers(ce.IfTrue, param, colPath, ref foundProp) &&
               CheckExpressionMembers(ce.IfFalse, param, colPath, ref foundProp);

      default:
        return false;
    }
  }
}
