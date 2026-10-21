using System.Linq.Expressions;
using Air.App.Internal;
using Air.App.Modules;
using Air.Data.Contracts;
using Air.Data.Entities;
using Air.Data.Querying;
using Microsoft.EntityFrameworkCore;

namespace Air.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  private IQueryable<TEntity> BuildBaseEntityQuery(GridRequest? gridReq, out Parent parent, out Query queryDeclaration)
  {
    parent = new Parent();
    SetParent(parent);

    ParentId = gridReq?.ParentId;

    queryDeclaration = new Query();
    SetQuery(queryDeclaration);

    var query = BaseQuery(db);
    foreach (var filter in queryDeclaration.Filters)
    {
      query = query.Where(filter);
    }

    if (ParentId.HasValue && parent.FilterFunc != null)
    {
      query = parent.FilterFunc(query, ParentId.Value);
    }

    return query;
  }

  private static IQueryable<TEntity> ApplyActiveNamedFilters(
    IQueryable<TEntity> query,
    GridRequest? gridReq,
    Query queryDeclaration,
    NamedFilterItem? overrideToggle = null,
    NamedFilterGroup? overrideGroup = null,
    NamedFilterItem? overrideGroupItem = null)
  {
    var isSelect = gridReq?.ReferenceLabelPaths is { Count: > 0 } || (gridReq?.IsReferencePicker ?? false);
    if (isSelect) return query;

    var nfs = gridReq?.NamedFilters ?? [];

    foreach (var group in queryDeclaration.NamedFilterGroups)
    {
      NamedFilterItem? chosen;
      if (ReferenceEquals(group, overrideGroup))
      {
        chosen = overrideGroupItem;
      }
      else
      {
        var reqKey = nfs.FirstOrDefault(k => group.Items.Any(i => string.Equals(i.Key, k, StringComparison.OrdinalIgnoreCase)));
        chosen = reqKey != null
          ? group.Items.FirstOrDefault(i => string.Equals(i.Key, reqKey, StringComparison.OrdinalIgnoreCase))
          : group.Items.FirstOrDefault(i => i.IsDefault);
      }

      if (chosen?.Predicate != null)
      {
        query = query.Where(chosen.Predicate);
      }
    }

    foreach (var nf in queryDeclaration.NamedFilters)
    {
      var isActive = ReferenceEquals(nf, overrideToggle)
        || nfs.Any(k => string.Equals(k, nf.Key, StringComparison.OrdinalIgnoreCase));
      if (isActive && nf.Predicate != null)
      {
        query = query.Where(nf.Predicate);
      }
    }

    return query;
  }

  private IQueryable<TEntity> BuildFilteredEntityQuery(GridRequest? gridReq, out Parent parent, out Query queryDeclaration)
  {
    var query = BuildBaseEntityQuery(gridReq, out parent, out queryDeclaration);
    return ApplyActiveNamedFilters(query, gridReq, queryDeclaration);
  }

  public virtual async Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default)
  {
    var gridReq = request as GridRequest;
    var query = BuildFilteredEntityQuery(gridReq, out _, out var queryDeclaration);
    var cols = CreateColumns();

    query = IncludeColumnNavigations(query, cols.List);

    var isSelect = gridReq?.ReferenceLabelPaths is { Count: > 0 } || (gridReq?.IsReferencePicker ?? false);
    if (isSelect)
    {
      var labelPaths = gridReq?.ReferenceLabelPaths is { Count: > 0 } ? gridReq.ReferenceLabelPaths : ["name"];
      var patchColumns = gridReq?.PatchSourceColumns ?? [];
      var preserved = new HashSet<string>(labelPaths, StringComparer.OrdinalIgnoreCase);
      foreach (var p in patchColumns) preserved.Add(p);
      cols.RetainOnly(preserved);
    }

    if (request.PageSize <= 0 && queryDeclaration.CustomPageSize is > 0)
    {
      request.PageSize = queryDeclaration.CustomPageSize.Value;
    }

    if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
    {
      request.Sort = queryDeclaration.DefaultSort.ToList();
    }

    if (!string.IsNullOrWhiteSpace(request.Search) && queryDeclaration.PickKeySelector != null)
    {
      request.Search = queryDeclaration.PickKeySelector(request.Search);
    }

    if (request.SearchFields is not { Count: > 0 } && queryDeclaration.SearchFields is { Count: > 0 })
    {
      request.SearchFields = queryDeclaration.SearchFields.ToList();
    }

    if (!isSelect && !(gridReq?.IsDataOnly ?? false) && request.Filter is null && request.ColumnFilter is null && (gridReq?.NamedFilters is not { Count: > 0 }) && queryDeclaration.InitialFilters.Count > 0)
    {
      request.Filter = queryDeclaration.InitialFilters.Count == 1
        ? queryDeclaration.InitialFilters[0]
        : FilterNode.All([.. queryDeclaration.InitialFilters.Cast<FilterNode>()]);
    }

    var queryMap = BuildQueryMap(queryDeclaration, cols.List);

    if (queryDeclaration.Selector != null)
    {
      var dataProvider = DataProvider.Create(query, queryMap, queryDeclaration.Selector);
      var page = await dataProvider.GetPageAsync(request, ct);
      if (queryDeclaration.ComputeAction != null && page.Rows.Count > 0)
      {
        await queryDeclaration.ComputeAction(page.Rows, ct);
      }
      var shapedRows = page.Rows.Select(r => ShapeRow(r, cols.List, queryDeclaration.RowFlags)).ToList();
      await PopulateCoversAsync(shapedRows, cols.List, ct);
      await PopulateProjectedColorsAsync(shapedRows, cols.List, ct);
      await PopulateRowAvailabilityAsync(shapedRows, cols, ct);
      var filteredEntities = dataProvider.Filter(request);
      var bottomRows = !isSelect ? await ComputeSumRowsAsync(filteredEntities.Select(queryDeclaration.Selector), cols.List, ct) : null;
      return new Page<object>
      {
        Rows = shapedRows,
        BottomRows = bottomRows,
        Forms = CollectRowFormOverlays(shapedRows),
        Total = page.Total,
        Number = page.Number,
        Size = page.Size,
        HasMore = page.HasMore
      };
    }

    var defaultDataProvider = DataProvider.Create(query, queryMap);
    var defaultPage = await defaultDataProvider.GetPageAsync(request, ct);
    var defaultShapedRows = defaultPage.Rows.Select(r => ShapeRow((r as TRow)!, cols.List, queryDeclaration.RowFlags)).ToList();
    await PopulateCoversAsync(defaultShapedRows, cols.List, ct);
    await PopulateRowAvailabilityAsync(defaultShapedRows, cols, ct);
    var defaultBottomRows = !isSelect ? await ComputeSumRowsAsync(defaultDataProvider.Filter(request), cols.List, ct) : null;

    return new Page<object>
    {
      Rows = defaultShapedRows,
      BottomRows = defaultBottomRows,
      Forms = CollectRowFormOverlays(defaultShapedRows),
      Total = defaultPage.Total,
      Number = defaultPage.Number,
      Size = defaultPage.Size,
      HasMore = defaultPage.HasMore
    };
  }

  public virtual async Task<GridCountDto> GetCountAsync(GridRequest request, CancellationToken ct = default)
  {
    var baseEntityQuery = BuildBaseEntityQuery(request, out _, out var queryDeclaration);
    var query = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration);
    var cols = CreateColumns();

    if (!string.IsNullOrWhiteSpace(request.Search) && queryDeclaration.PickKeySelector != null)
    {
      request.Search = queryDeclaration.PickKeySelector(request.Search);
    }

    if (request.SearchFields is not { Count: > 0 } && queryDeclaration.SearchFields is { Count: > 0 })
    {
      request.SearchFields = queryDeclaration.SearchFields.ToList();
    }

    var queryMap = BuildQueryMap(queryDeclaration, cols.List);

    Task<int> CountWithAsync(IQueryable<TEntity> q, CancellationToken token) =>
      queryDeclaration.Selector != null
        ? DataProvider.Create(q, queryMap, queryDeclaration.Selector).Filter(request).CountAsync(token)
        : DataProvider.Create(q, queryMap).Filter(request).CountAsync(token);

    var total = await CountWithAsync(query, ct);

    Dictionary<string, int>? badges = null;
    foreach (var group in queryDeclaration.NamedFilterGroups)
    {
      foreach (var item in group.Items)
      {
        if (item.HasBadge)
        {
          badges ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
          var badgeQuery = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration, overrideGroup: group, overrideGroupItem: item);
          badges[item.Key] = await CountWithAsync(badgeQuery, ct);
        }
      }
    }

    foreach (var item in queryDeclaration.NamedFilters)
    {
      if (item.HasBadge)
      {
        badges ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var badgeQuery = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration, overrideToggle: item);
        badges[item.Key] = await CountWithAsync(badgeQuery, ct);
      }
    }

    return new GridCountDto { Total = total, NamedFilterBadges = badges };
  }

  private async Task PopulateRowAvailabilityAsync(List<object> shapedRows, Columns columns, CancellationToken ct)
  {
    if (shapedRows.Count == 0 || columns.StatusFlow is not Screens.IRecordStatuses<TEntity> statusFlow) return;
    var transitionsWithOffers = statusFlow.Transitions.Where(t => t.Offers.Count > 0).ToList();
    if (transitionsWithOffers.Count == 0) return;

    var rowIds = ExtractRowIds(shapedRows);
    if (rowIds.Count == 0) return;

    foreach (var transition in transitionsWithOffers)
    {
      var actionKey = $"status.{transition.Name.ToLowerInvariant()}";
      for (int i = 0; i < transition.Offers.Count; i++)
      {
        if (transition.Offers[i].Condition is not Expression<Func<TEntity, bool>> cond) continue;

        var passedIds = (await db.Set<TEntity>()
          .Where(e => rowIds.Contains(e.Id))
          .Where(cond)
          .Select(e => e.Id)
          .ToListAsync(ct)).ToHashSet();

        foreach (var rowObj in shapedRows)
        {
          if (rowObj is not Dictionary<string, object?> dict || !dict.TryGetValue("id", out var idVal) || idVal == null) continue;
          if (!long.TryParse(idVal.ToString(), out var parsedId)) continue;
          if (!passedIds.Contains(parsedId))
          {
            if (!dict.TryGetValue("unavailable", out var unavObj) || unavObj is not Dictionary<string, int> unav)
            {
              unav = new Dictionary<string, int>(StringComparer.Ordinal);
              dict["unavailable"] = unav;
            }
            unav.TryAdd(actionKey, i);
          }
        }
      }
    }
  }

  private async Task PopulateCoversAsync(List<object> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
  {
    var coverCol = columns.FirstOrDefault(c => c.Cell is AddOns.Covers.Contracts.GridCellCoverModel);
    if (coverCol == null || shapedRows.Count == 0) return;

    var coverCell = (AddOns.Covers.Contracts.GridCellCoverModel)coverCol.Cell!;
    var ownerIds = ExtractRowIds(shapedRows);
    if (ownerIds.Count == 0) return;

    var fileGridType = ModuleCatalog.Instance.FindServiceType(coverCell.Service);
    if (fileGridType != null && typeof(Files.Internal.IFileCoverSource).IsAssignableFrom(fileGridType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(fileGridType, db, _app) is Files.Internal.IFileCoverSource coverSource)
        {
          var covers = await coverSource.CoversAsync(ownerIds, ct);
          foreach (var rowObj in shapedRows)
          {
            if (rowObj is Dictionary<string, object?> dict && dict.TryGetValue("id", out var idVal) && idVal != null)
            {
              if (long.TryParse(idVal.ToString(), out var parsedId) && covers.TryGetValue(parsedId, out var coverDto))
              {
                dict[coverCol.Path ?? "idCover"] = coverDto;
              }
            }
          }
        }
      }
      catch
      {
      }
    }
  }

  private async Task PopulateProjectedColorsAsync(List<object> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
  {
    if (typeof(TRow) == typeof(TEntity) || shapedRows.Count == 0) return;

    var colorCols = columns.Where(c => c.ColorEvaluator != null && !string.IsNullOrEmpty(c.Path)).ToList();
    if (colorCols.Count == 0) return;

    var rowIds = ExtractRowIds(shapedRows);
    if (rowIds.Count == 0) return;

    var entityQuery = IncludeColumnNavigations(db.Set<TEntity>().AsNoTracking(), colorCols)
      .Where(e => rowIds.Contains(e.Id));
    var entitiesById = await entityQuery.ToDictionaryAsync(e => e.Id, ct);

    foreach (var rowObj in shapedRows)
    {
      if (rowObj is not Dictionary<string, object?> dict || !dict.TryGetValue("id", out var idVal) || idVal == null) continue;
      if (!long.TryParse(idVal.ToString(), out var parsedId) || !entitiesById.TryGetValue(parsedId, out var entity)) continue;

      foreach (var col in colorCols)
      {
        var hue = col.ColorEvaluator!(entity)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(hue) || string.IsNullOrEmpty(col.Path)) continue;

        var key = ToCamel(col.Path.Split('.')[0]);
        if (dict.TryGetValue(key, out var val))
        {
          if (val is Reference r)
          {
            dict[key] = new Dictionary<string, object?>
            {
              ["id"] = r.Id,
              ["name"] = r.Name,
              ["color"] = hue
            };
          }
          else if (val is Dictionary<string, object?> dictVal)
          {
            dictVal["color"] = hue;
          }
          else
          {
            dict[$"{col.Path}Color"] = hue;
          }
        }
      }
    }
  }

  private static List<long> ExtractRowIds(List<object> shapedRows)
  {
    var ids = new List<long>(shapedRows.Count);
    foreach (var rowObj in shapedRows)
    {
      if (rowObj is Dictionary<string, object?> dict && dict.TryGetValue("id", out var idVal) && idVal != null)
      {
        if (idVal is long l)
        {
          ids.Add(l);
        }
        else if (long.TryParse(idVal.ToString(), out var parsedId))
        {
          ids.Add(parsedId);
        }
      }
    }
    return ids;
  }

  private static async Task<List<object>?> ComputeSumRowsAsync<TSource>(IQueryable<TSource> filteredQuery, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
  {
    var sumCols = columns.Where(c => c.IsSummed && !string.IsNullOrEmpty(c.Path)).ToList();
    if (sumCols.Count == 0) return null;

    var sumRow = new Dictionary<string, object?>(StringComparer.Ordinal);
    var rowParam = Expression.Parameter(typeof(TSource), "r");

    foreach (var col in sumCols)
    {
      if (string.IsNullOrEmpty(col.Path)) continue;

      Expression? current = rowParam;
      var currentType = typeof(TSource);
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

      if (current == null) continue;
      var underlying = Nullable.GetUnderlyingType(currentType) ?? currentType;

      try
      {
        if (underlying == typeof(decimal))
        {
          var expr = Expression.Lambda<Func<TSource, decimal?>>(
            currentType == typeof(decimal?) ? current : Expression.Convert(current, typeof(decimal?)),
            rowParam);
          var total = await filteredQuery.SumAsync(expr, ct) ?? 0m;
          PutValue(sumRow, col.Path.Split('.'), total);
        }
        else if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short))
        {
          var expr = Expression.Lambda<Func<TSource, long?>>(
            currentType == typeof(long?) ? current : Expression.Convert(current, typeof(long?)),
            rowParam);
          var total = await filteredQuery.SumAsync(expr, ct) ?? 0L;
          PutValue(sumRow, col.Path.Split('.'), total);
        }
        else if (underlying == typeof(double) || underlying == typeof(float))
        {
          var expr = Expression.Lambda<Func<TSource, double?>>(
            currentType == typeof(double?) ? current : Expression.Convert(current, typeof(double?)),
            rowParam);
          var total = await filteredQuery.SumAsync(expr, ct) ?? 0d;
          PutValue(sumRow, col.Path.Split('.'), total);
        }
      }
      catch { }
    }

    return sumRow.Count > 0 ? [sumRow] : null;
  }

  private static IQueryable<TEntity> IncludeColumnNavigations(IQueryable<TEntity> query, IEnumerable<GridColumnDto> columns)
  {
    var entityType = typeof(TEntity);
    foreach (var col in columns)
    {
      if (string.IsNullOrEmpty(col.Path)) continue;

      var parts = col.Path.Split('.');
      if (col.ReferenceService != null)
      {
        var actualNavPath = ResolvePropertyPath(entityType, parts);
        if (actualNavPath != null)
        {
          try { query = query.Include(actualNavPath); } catch { }
        }
      }
      else if (parts.Length > 1)
      {
        var actualNavPath = ResolvePropertyPath(entityType, parts[..^1]);
        if (actualNavPath != null)
        {
          try { query = query.Include(actualNavPath); } catch { }
        }
      }

      if (!string.IsNullOrEmpty(col.ColorNavPath))
      {
        var actualColorNavPath = ResolvePropertyPath(entityType, col.ColorNavPath.Split('.'));
        if (actualColorNavPath != null)
        {
          try { query = query.Include(actualColorNavPath); } catch { }
        }
      }
    }

    return query;
  }

  private static string? ResolvePropertyPath(Type rootType, string[] segments)
  {
    var currentType = rootType;
    var validProps = new List<string>(segments.Length);
    foreach (var segment in segments)
    {
      var prop = PropertyAccessorCache.GetProperty(currentType, segment);
      if (prop == null) return null;
      validProps.Add(prop.Name);
      currentType = prop.PropertyType;
    }

    return validProps.Count > 0 ? string.Join('.', validProps) : null;
  }

  private Dictionary<string, object?>? CollectRowFormOverlays(List<object> shapedRows)
  {
    Dictionary<string, object?>? forms = null;
    foreach (var rowObj in shapedRows)
    {
      if (rowObj is Dictionary<string, object?> dict &&
          dict.TryGetValue("form", out var formObj) &&
          formObj is string formHandle &&
          !string.IsNullOrEmpty(formHandle) &&
          (forms == null || !forms.ContainsKey(formHandle)))
      {
        var formType = ModuleCatalog.Instance.FindServiceType(formHandle);
        if (formType != null && typeof(Form.IFormService).IsAssignableFrom(formType))
        {
          try
          {
            if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, _app) is Form.IFormService formInstance)
            {
              forms ??= new Dictionary<string, object?>(StringComparer.Ordinal);
              forms[formHandle] = formInstance.Overlay(formHandle);
            }
          }
          catch { }
        }
      }
    }
    return forms;
  }

  internal static QueryMap<TEntity> BuildQueryMap(Query queryDeclaration, IReadOnlyList<GridColumnDto> columns)
  {
    var builder = queryDeclaration.Selector is not null
      ? QueryMap.FromSelector(queryDeclaration.Selector)
      : QueryMap.FromEntity<TEntity>();

    builder.AddEntityProperties();

    var entityParam = Expression.Parameter(typeof(TEntity), "e");

    foreach (var col in columns)
    {
      if (string.IsNullOrEmpty(col.Path)) continue;

      if (col.FilterExpression is not null)
      {
        var filterKey = !string.IsNullOrEmpty(col.FilterPath) ? col.FilterPath : col.Path;
        builder.Field(filterKey, col.FilterExpression, f => f.FilterOnly());
        if (!string.Equals(filterKey, col.Path, StringComparison.OrdinalIgnoreCase))
        {
          builder.Field(col.Path, col.FilterExpression, f => f.FilterOnly());
        }
        continue;
      }

      var isRef = col.ReferenceService != null || col.Cell is GridCellReferenceModel || typeof(Reference).IsAssignableFrom(col.MemberType);
      if (isRef)
      {
        var nameExpr = builder.FindExpression(col.Path + ".Name")
          ?? BuildEntityMemberExpression(entityParam, col.Path, preferNameOnObject: true);
        if (nameExpr != null)
        {
          builder.Field(col.Path, nameExpr);
        }

        if (!string.IsNullOrEmpty(col.FilterPath) && !string.Equals(col.FilterPath, col.Path, StringComparison.OrdinalIgnoreCase))
        {
          var idExpr = builder.FindExpression(col.FilterPath)
            ?? builder.FindExpression(col.Path + ".Id")
            ?? BuildEntityMemberExpression(entityParam, col.FilterPath, preferNameOnObject: false);
          if (idExpr != null)
          {
            builder.Field(col.FilterPath, idExpr);
          }
        }
        continue;
      }

      if (builder.FindExpression(col.Path) is null)
      {
        var expr = BuildEntityMemberExpression(entityParam, col.Path, preferNameOnObject: true);
        if (expr is not null)
        {
          builder.Field(col.Path, expr);
        }
      }
    }

    if (queryDeclaration.SearchFields is { Count: > 0 })
    {
      foreach (var sf in queryDeclaration.SearchFields)
      {
        if (builder.FindExpression(sf) is not null)
        {
          try
          {
            builder.Configure(sf, f => f.Searchable());
          }
          catch { }
        }
      }
    }

    foreach (var rule in queryDeclaration.DefaultSort)
    {
      if (builder.FindExpression(rule.Field) is null)
      {
        var sortExpr = BuildEntityMemberExpression(entityParam, rule.Field, preferNameOnObject: true);
        if (sortExpr is not null)
        {
          builder.Field(rule.Field, sortExpr, f => f.SortOnly());
        }
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

  private static bool IsScalarType(Type t)
  {
    var u = Nullable.GetUnderlyingType(t) ?? t;
    return u.IsValueType || u == typeof(string);
  }
}
