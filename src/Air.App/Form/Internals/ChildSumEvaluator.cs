using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Form.Internals;

internal static class ChildSumEvaluator
{
  private static readonly MethodInfo ApplyChildSumsGenericMethod =
    typeof(ChildSumEvaluator).GetMethod(nameof(ApplyChildSumsForEntityAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

  public static async Task ApplySumsAsync<TEntity>(
    PlatformDbContext db,
    PlatformAppService? app,
    TEntity parentEntity,
    IReadOnlyList<ChildGridBuilder> sumChildren,
    CancellationToken ct)
    where TEntity : class, IEntity
  {
    if (parentEntity.Id <= 0 || sumChildren.Count == 0)
    {
      return;
    }

    foreach (var child in sumChildren)
    {
      if (child.IsFiles || child.SumMappings.Count == 0)
      {
        continue;
      }

      var childEntityType = child.ChildEntityType ?? ParentScopeValidator.GetGridEntityType(child.ServiceType);
      if (childEntityType == null || !typeof(IEntity).IsAssignableFrom(childEntityType))
      {
        continue;
      }

      var task = (Task)ApplyChildSumsGenericMethod
        .MakeGenericMethod(typeof(TEntity), childEntityType)
        .Invoke(null, [db, app, parentEntity, child, ct])!;
      await task;
    }
  }

  private static async Task ApplyChildSumsForEntityAsync<TEntity, TChildEntity>(
    PlatformDbContext db,
    PlatformAppService? app,
    TEntity parentEntity,
    ChildGridBuilder child,
    CancellationToken ct)
    where TEntity : class, IEntity
    where TChildEntity : class, IEntity
  {
    IQueryable<TChildEntity>? query = null;
    if (child.FilterProvider != null)
    {
      query = (IQueryable<TChildEntity>)child.FilterProvider(db, parentEntity.Id, db.Set<TChildEntity>().AsNoTracking());
    }
    else if (child.ServiceType != null &&
             ModuleCatalog.Instance.CreateServiceInstance(child.ServiceType, db, app) is Grid.IGridService childGrid &&
             childGrid.GetBoundParentProperty() is { } boundProp)
    {
      object parentVal = boundProp.PropertyType == typeof(long?) ? (long?)parentEntity.Id : parentEntity.Id;
      query = db.Set<TChildEntity>().AsNoTracking().Where(ParentScopeValidator.BuildParentKeyFilter<TChildEntity>(boundProp, parentVal));
    }

    if (query == null)
    {
      return;
    }

    var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
    var rowParam = Expression.Parameter(typeof(TChildEntity), "c");

    foreach (var mapping in child.SumMappings)
    {
      var parentProp = mapping.ParentProperty ?? PropertyAccessorCache.GetProperty(typeof(TEntity), mapping.ParentFieldPath);
      if (parentProp is not { CanWrite: true })
      {
        continue;
      }

      var (valueExpr, valueType) = ResolveChildExpression(mapping, typeof(TChildEntity), rowParam);
      if (valueExpr == null || valueType == null)
      {
        continue;
      }

      var childUnderlying = Nullable.GetUnderlyingType(valueType) ?? valueType;
      object? computedValue = null;

      if (childUnderlying == typeof(decimal))
      {
        if (isSqlite)
        {
          var dblBody = Expression.Convert(valueExpr, typeof(double?));
          var dblSum = await query.SumAsync(Expression.Lambda<Func<TChildEntity, double?>>(dblBody, rowParam), ct);
          computedValue = dblSum.HasValue ? Math.Round(Convert.ToDecimal(dblSum.Value, CultureInfo.InvariantCulture), 6) : 0m;
        }
        else
        {
          var decBody = valueType == typeof(decimal?) ? valueExpr : Expression.Convert(valueExpr, typeof(decimal?));
          computedValue = await query.SumAsync(Expression.Lambda<Func<TChildEntity, decimal?>>(decBody, rowParam), ct) ?? 0m;
        }
      }
      else if (childUnderlying == typeof(int) ||
               childUnderlying == typeof(long) ||
               childUnderlying == typeof(short) ||
               childUnderlying == typeof(byte))
      {
        var longBody = valueType == typeof(long?) ? valueExpr : Expression.Convert(valueExpr, typeof(long?));
        computedValue = await query.SumAsync(Expression.Lambda<Func<TChildEntity, long?>>(longBody, rowParam), ct) ?? 0L;
      }
      else if (childUnderlying == typeof(double) || childUnderlying == typeof(float))
      {
        var dblBody = valueType == typeof(double?) ? valueExpr : Expression.Convert(valueExpr, typeof(double?));
        computedValue = await query.SumAsync(Expression.Lambda<Func<TChildEntity, double?>>(dblBody, rowParam), ct) ?? 0d;
      }

      if (computedValue != null && TryConvertNumeric(computedValue, parentProp.PropertyType, out var targetVal))
      {
        parentProp.SetValue(parentEntity, targetVal);
      }
    }
  }

  private static (Expression? Body, Type? ValueType) ResolveChildExpression(
    ChildSumMapping mapping,
    Type childEntityType,
    ParameterExpression rowParam)
  {
    if (mapping.ChildSelector != null)
    {
      var rebound = ParameterRebinder.Rebind(mapping.ChildSelector, rowParam);
      return (rebound, mapping.ChildSelector.ReturnType);
    }

    Expression current = rowParam;
    var currentType = childEntityType;
    foreach (var seg in mapping.ChildFieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
    {
      var prop = PropertyAccessorCache.GetProperty(currentType, seg);
      if (prop == null)
      {
        return (null, null);
      }

      current = Expression.Property(current, prop);
      currentType = prop.PropertyType;
    }

    return (current, currentType);
  }

  private static bool TryConvertNumeric(object raw, Type targetPropertyType, out object? converted)
  {
    var targetType = Nullable.GetUnderlyingType(targetPropertyType) ?? targetPropertyType;
    try
    {
      converted = targetType switch
      {
        _ when targetType == typeof(decimal) => Convert.ToDecimal(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(long) => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(int) => Convert.ToInt32(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(short) => Convert.ToInt16(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(byte) => Convert.ToByte(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(double) => Convert.ToDouble(raw, CultureInfo.InvariantCulture),
        _ when targetType == typeof(float) => Convert.ToSingle(raw, CultureInfo.InvariantCulture),
        _ => null
      };
      return converted != null;
    }
    catch
    {
      converted = null;
      return false;
    }
  }
}
