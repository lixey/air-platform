using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridQueryMapBuilder<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  private static readonly ConcurrentDictionary<string, QueryMap<TEntity>> CachedQueryMaps = new(StringComparer.Ordinal);

  public static QueryMap<TEntity> Build(
    Type serviceType,
    GridQuery<TEntity, TRow> queryDeclaration,
    IReadOnlyList<GridColumnDto> columns,
    bool useCollationComparison = false)
  {
    var allowedFilterSig = ReferenceLookupAllowlist.GetAllowedFilterSignature(serviceType);
    if (queryDeclaration.Selector is null)
    {
      var sb = new StringBuilder(columns.Count * 32 + 96);
      sb.Append(serviceType.FullName).Append('|')
        .Append(typeof(TEntity).FullName).Append('|')
        .Append(typeof(TRow).FullName).Append('|')
        .Append(allowedFilterSig).Append('|')
        .Append(useCollationComparison ? 'C' : 'L').Append('#');
      var canCache = true;
      for (var i = 0; i < columns.Count; i++)
      {
        var col = columns[i];
        if (col.FilterExpression is not null || col.IsSubqueryExpression)
        {
          canCache = false;
          break;
        }

        var isRef = col.ReferenceService != null || col.Cell is GridCellReferenceModel || typeof(Reference).IsAssignableFrom(col.MemberType);
        sb.Append(col.Path).Append('|')
          .Append(col.FilterPath).Append('|')
          .Append(col.ValueExpression?.ToString()).Append('|')
          .Append(isRef ? '1' : '0').Append('|')
          .Append(col.IsHidden ? '1' : '0').Append('|')
          .Append(col.IsSortable ? '1' : '0').Append('|')
          .Append(col.IsFilterable ? '1' : '0');
        if (col.ReferenceLabelPaths is { Count: > 0 } refLabels)
        {
          for (var j = 0; j < refLabels.Count; j++)
          {
            sb.Append(',').Append(refLabels[j]);
          }
        }

        sb.Append(';');
      }

      if (canCache)
      {
        if (queryDeclaration.SearchFields is { Count: > 0 } sfs)
        {
          sb.Append("#S:");
          for (var i = 0; i < sfs.Count; i++)
          {
            sb.Append(sfs[i]).Append(',');
          }
        }

        if (queryDeclaration.DefaultSort.Count > 0)
        {
          sb.Append("#D:");
          for (var i = 0; i < queryDeclaration.DefaultSort.Count; i++)
          {
            sb.Append(queryDeclaration.DefaultSort[i].Field)
              .Append(':')
              .Append(queryDeclaration.DefaultSort[i].Direction)
              .Append(',');
          }
        }

        if (CachedQueryMaps.Count > 512)
        {
          CachedQueryMaps.Clear();
        }

        return CachedQueryMaps.GetOrAdd(sb.ToString(), _ => BuildCore(serviceType, queryDeclaration, columns, useCollationComparison));
      }
    }

    return BuildCore(serviceType, queryDeclaration, columns, useCollationComparison);
  }

  private static QueryMap<TEntity> BuildCore(
    Type serviceType,
    GridQuery<TEntity, TRow> queryDeclaration,
    IReadOnlyList<GridColumnDto> columns,
    bool useCollationComparison)
  {
    Dictionary<string, LambdaExpression>? selectorFields = null;
    if (queryDeclaration.Selector is not null)
    {
      selectorFields = new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase);
      foreach (var (name, expr) in InferredFields.FromSelector(queryDeclaration.Selector))
      {
        selectorFields[name] = expr;
      }
    }

    var builder = QueryMap.For<TEntity>();
    builder.UseCollationComparison(useCollationComparison);

    var entityParam = Expression.Parameter(typeof(TEntity), "e");
    LambdaExpression? ResolveField(string path, bool preferNameOnObject) =>
      selectorFields?.GetValueOrDefault(path) ?? BuildEntityMemberExpression(entityParam, path, preferNameOnObject);

    var idFieldExpr = ResolveField("id", preferNameOnObject: false) ?? ((Expression<Func<TEntity, long>>)(e => e.Id));
    builder.Field("id", idFieldExpr, f => f.Searchable(false));

    var hasExplicitSearchFields = queryDeclaration.SearchFields is { Count: > 0 };

    void ApplyColumnCapabilities(string fieldName, GridColumnDto col, bool searchableString = false)
    {
      if (builder.FindExpression(fieldName) is null)
      {
        return;
      }

      var isDefaultSort = queryDeclaration.DefaultSort.Any(s => string.Equals(s.Field, fieldName, StringComparison.OrdinalIgnoreCase));
      builder.Configure(fieldName, f =>
      {
        f.Searchable(searchableString);
        if (!col.IsSortable && !isDefaultSort)
        {
          f.Sortable(false);
        }

        if (!col.IsFilterable)
        {
          f.Filterable(false);
        }
      });
    }

    foreach (var col in columns)
    {
      if (string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

      if (col.FilterExpression is not null)
      {
        var filterKey = !string.IsNullOrEmpty(col.FilterPath) ? col.FilterPath : col.Path;
        if (col.IsFilterable)
        {
          builder.Field(filterKey, col.FilterExpression, f => f.FilterOnly());
          if (!string.Equals(filterKey, col.Path, StringComparison.OrdinalIgnoreCase))
          {
            builder.Field(col.Path, col.FilterExpression, f => f.FilterOnly());
          }
        }

        continue;
      }

      if (col.ValueExpression is not null)
      {
        builder.Field(col.Path, col.ValueExpression);
        ApplyColumnCapabilities(
          col.Path,
          col,
          searchableString: !hasExplicitSearchFields && !col.IsHidden && col.ValueExpression.ReturnType == typeof(string));
        continue;
      }

      var isRef = col.ReferenceService != null || col.Cell is GridCellReferenceModel || typeof(Reference).IsAssignableFrom(col.MemberType);
      if (isRef)
      {
        var primaryLabelProp = col.ReferenceLabelPaths is { Count: > 0 } ? col.ReferenceLabelPaths[0] : null;
        var nameExpr = (primaryLabelProp != null
            ? builder.FindExpression($"{col.Path}.{primaryLabelProp}") ?? ResolveField($"{col.Path}.{primaryLabelProp}", preferNameOnObject: true)
            : null)
          ?? builder.FindExpression(col.Path + ".Name")
          ?? ResolveField(col.Path + ".Name", preferNameOnObject: false)
          ?? ResolveField(col.Path, preferNameOnObject: true);
        if (nameExpr != null)
        {
          builder.Field(col.Path, nameExpr);
          ApplyColumnCapabilities(
            col.Path,
            col,
            searchableString: !hasExplicitSearchFields && !col.IsHidden && nameExpr.ReturnType == typeof(string));
        }

        if (!string.IsNullOrEmpty(col.FilterPath) && !string.Equals(col.FilterPath, col.Path, StringComparison.OrdinalIgnoreCase))
        {
          var idExpr = builder.FindExpression(col.FilterPath)
            ?? ResolveField(col.FilterPath, preferNameOnObject: false)
            ?? ResolveField(col.Path + ".Id", preferNameOnObject: false);
          if (idExpr != null)
          {
            builder.Field(col.FilterPath, idExpr);
            ApplyColumnCapabilities(col.FilterPath, col, searchableString: false);
          }
        }

        continue;
      }

      var existingExpr = builder.FindExpression(col.Path);
      if (existingExpr is null)
      {
        var expr = ResolveField(col.Path, preferNameOnObject: true);
        if (expr is not null)
        {
          builder.Field(col.Path, expr);
          existingExpr = expr;
        }
      }

      if (existingExpr is not null)
      {
        ApplyColumnCapabilities(
          col.Path,
          col,
          searchableString: !hasExplicitSearchFields && !col.IsHidden && existingExpr.ReturnType == typeof(string));
      }
    }

    if (queryDeclaration.SearchFields is { Count: > 0 })
    {
      foreach (var sf in queryDeclaration.SearchFields)
      {
        if (builder.FindExpression(sf) is not null)
        {
          builder.Configure(sf, f => f.Searchable());
        }
        else if (ResolveField(sf, preferNameOnObject: true) is { } searchExpr)
        {
          builder.Field(sf, searchExpr, f => f.SearchOnly());
        }
      }
    }

    foreach (var rule in queryDeclaration.DefaultSort)
    {
      if (builder.FindExpression(rule.Field) is null &&
          ResolveField(rule.Field, preferNameOnObject: true) is { } sortExpr)
      {
        builder.Field(rule.Field, sortExpr, f => f.SortOnly());
      }
    }

    foreach (var allowedFilterField in ReferenceLookupAllowlist.GetAllowedFilterFields(serviceType))
    {
      if (builder.FindExpression(allowedFilterField) is null &&
          ResolveField(allowedFilterField, preferNameOnObject: false) is { } filterExpr)
      {
        builder.Field(allowedFilterField, filterExpr, f => f.FilterOnly());
      }
    }

    return builder.Build();
  }

  private static LambdaExpression? BuildEntityMemberExpression(ParameterExpression entityParam, string path, bool preferNameOnObject)
  {
    var segments = path.Split('.');
    Expression? current = entityParam;
    var currentType = typeof(TEntity);

    foreach (var segment in segments)
    {
      var prop = PropertyAccessorCache.GetProperty(currentType, segment);
      if (prop is null)
      {
        return null;
      }

      current = Expression.Property(current, prop);
      currentType = prop.PropertyType;
    }

    if (!IsScalarType(currentType))
    {
      if (preferNameOnObject)
      {
        var nameProp = PropertyAccessorCache.GetProperty(currentType, "Name");
        if (nameProp != null && IsScalarType(nameProp.PropertyType))
        {
          current = Expression.Property(current, nameProp);
          return Expression.Lambda(current, entityParam);
        }
      }

      return null;
    }

    return Expression.Lambda(current, entityParam);
  }

  internal static bool IsScalarType(Type t)
  {
    var u = Nullable.GetUnderlyingType(t) ?? t;
    return u.IsValueType || u == typeof(string);
  }
}
