using Platform.App.Grid.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
{
  internal static QueryMap<TEntity> BuildQueryMap(
    Type serviceType,
    GridQuery<TEntity, TRow> queryDeclaration,
    IReadOnlyList<GridColumnDto> columns,
    bool useCollationComparison = false) =>
    GridQueryMapBuilder<TEntity, TRow>.Build(serviceType, queryDeclaration, columns, useCollationComparison);

  private IQueryable<TEntity> BuildBaseEntityQuery(
    GridRequest? gridReq,
    out GridParent<TEntity> parent,
    out GridQuery<TEntity, TRow> queryDeclaration,
    bool preserveParent = false)
  {
    parent = GetParent();

    if (gridReq != null)
    {
      ParentService = gridReq.ParentService;
      ParentId = gridReq.ParentId;
    }
    else if (!preserveParent)
    {
      ParentService = null;
      ParentId = null;
    }

    queryDeclaration = GetQueryDeclaration();

    var query = BaseQuery(db);
    var restrictions = BuildRestrictions();
    foreach (var restriction in restrictions.Filters)
    {
      query = query.Where(restriction);
    }

    foreach (var filter in queryDeclaration.Filters)
    {
      query = query.Where(filter);
    }

    if (ParentId.HasValue)
    {
      if (ParentId.Value <= 0 || !parent.MatchesService(ParentService))
      {
        query = query.Where(_ => false);
      }
      else if (parent.FilterFunc != null)
      {
        query = parent.FilterFunc(query, ParentId.Value);
      }
      else if (ResolveParentChildBuilder() is { FilterProvider: { } filterProvider })
      {
        query = (IQueryable<TEntity>)filterProvider(db, ParentId.Value, query);
      }
      else if (!parent.HasRead)
      {
        query = query.Where(_ => false);
      }
    }
    else if (parent.BoundProperty != null && !parent.IsOptional)
    {
      query = query.Where(_ => false);
    }

    return query;
  }

  private static IQueryable<TEntity> ApplyActiveNamedFilters(
    IQueryable<TEntity> query,
    GridRequest? gridReq,
    GridQuery<TEntity, TRow> queryDeclaration,
    GridNamedFilterItem<TEntity>? overrideToggle = null,
    GridNamedFilterGroup<TEntity>? overrideGroup = null,
    GridNamedFilterItem<TEntity>? overrideGroupItem = null)
  {
    var isSelect = gridReq?.ReferenceLabelPaths is { Count: > 0 } || (gridReq?.IsReferencePicker ?? false);
    if (isSelect)
    {
      return query;
    }

    var nfs = gridReq?.NamedFilters ?? [];

    foreach (var group in queryDeclaration.NamedFilterGroups)
    {
      GridNamedFilterItem<TEntity>? chosen;
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

  private IQueryable<TEntity> BuildFilteredEntityQuery(
    GridRequest? gridReq,
    out GridParent<TEntity> parent,
    out GridQuery<TEntity, TRow> queryDeclaration)
  {
    var query = BuildBaseEntityQuery(gridReq, out parent, out queryDeclaration);
    return ApplyActiveNamedFilters(query, gridReq, queryDeclaration);
  }

  public virtual async Task<GridPageDto<RowDto>> GetDataAsync(ListRequest request, CancellationToken ct = default)
  {
    var gridReq = request as GridRequest;
    ParentService = gridReq?.ParentService;
    ParentId = gridReq?.ParentId;
    var isReferenceSelect = gridReq?.ReferenceLabelPaths is { Count: > 0 };
    var isSelect = isReferenceSelect || (gridReq?.IsReferencePicker ?? false);
    if (!await EnsureParentScopeAsync(lockParent: false, ct))
    {
      return new GridPageDto<RowDto> { Rows = [], Total = 0, Number = request.Page, Size = request.PageSize, HasMore = false };
    }

    var baseEntityQuery = BuildBaseEntityQuery(gridReq, out _, out var queryDeclaration);
    var filteredQuery = ApplyActiveNamedFilters(baseEntityQuery, gridReq, queryDeclaration);
    var cols = await CreateColumnsAsync(ct);
    var allColumns = cols.List;
    var activeColumns = allColumns;

    if (isReferenceSelect)
    {
      var preserved = new HashSet<string>(gridReq!.ReferenceLabelPaths!, StringComparer.OrdinalIgnoreCase);
      foreach (var p in gridReq.PatchSourceColumns ?? [])
      {
        preserved.Add(p);
      }

      activeColumns = allColumns.Where(c => c.Path != null && preserved.Contains(c.Path)).ToList();
    }

    if (request.PageSize <= 0 && queryDeclaration.CustomPageSize is > 0)
    {
      request.PageSize = queryDeclaration.CustomPageSize.Value;
    }

    if (request.Sort is not { Count: > 0 } && queryDeclaration.DefaultSort.Count > 0)
    {
      request.Sort = queryDeclaration.DefaultSort.ToList();
    }

    NormalizeGridRequest(gridReq, request, queryDeclaration, isSelect);

    var queryMap = BuildQueryMap(GetType(), queryDeclaration, allColumns, IsSqlServerProvider);

    if (queryDeclaration.Selector != null)
    {
      var noTrackingFiltered = filteredQuery.AsNoTracking();
      var dataProvider = DataProvider.Create(noTrackingFiltered, queryMap, queryDeclaration.Selector);
      var page = await dataProvider.GetPageAsync(request, ct);
      var shapedRows = await GridRowShaper.ShapeAndEnrichBatchAsync(
        this,
        page.Rows,
        activeColumns,
        cols,
        queryDeclaration,
        includeScreenEnrichments: true,
        ct);
      var filteredEntities = DataProvider.Create(noTrackingFiltered, queryMap, queryDeclaration.Selector).Filter(request);
      var bottomRows = !isSelect ? await GridSummaryCalculator.ComputeSumRowsAsync(db, filteredEntities, queryDeclaration.Selector, activeColumns, ct) : null;
      var namedFilterCounts = !isSelect && request.Page <= 1
        ? await GridNamedFilterCounter.ComputeAsync(baseEntityQuery, gridReq, request, queryDeclaration, queryMap, page.Total, ct)
        : null;
      return new GridPageDto<RowDto>
      {
        Rows = shapedRows,
        BottomRows = bottomRows,
        Forms = await GridRowEnricher.CollectRowFormOverlaysAsync(this, shapedRows, ct),
        NamedFilterCounts = namedFilterCounts,
        Total = page.Total,
        Number = page.Number,
        Size = page.Size,
        HasMore = page.HasMore
      };
    }

    var defaultSelector = GridRowEnricher.CanUseDefaultProjection(this, filteredQuery, queryDeclaration)
      ? GridProjectionBuilder.TryBuildSelector<TEntity>(activeColumns, HasCustomShapeRow)
      : null;

    Page<TEntity> defaultPage;
    if (defaultSelector != null)
    {
      var projectedProvider = DataProvider.Create(filteredQuery.AsNoTracking(), queryMap, defaultSelector);
      defaultPage = await projectedProvider.GetPageAsync(request, ct);
    }
    else
    {
      var queryWithNavs = GridRowEnricher.IncludeColumnNavigations<TEntity, TRow>(filteredQuery.AsNoTrackingWithIdentityResolution(), allColumns);
      var defaultDataProvider = DataProvider.Create(queryWithNavs, queryMap);
      defaultPage = await defaultDataProvider.GetPageAsync(request, ct);
    }

    var defaultShapedRows = await GridRowShaper.ShapeAndEnrichBatchAsync(
      this,
      defaultPage.Rows,
      activeColumns,
      cols,
      queryDeclaration,
      includeScreenEnrichments: true,
      ct);
    var sumSourceQuery = DataProvider.Create(filteredQuery.AsNoTracking(), queryMap).Filter(request);
    var defaultBottomRows = !isSelect ? await GridSummaryCalculator.ComputeSumRowsAsync<TEntity, TRow>(db, sumSourceQuery, null, activeColumns, ct) : null;
    var defaultNamedFilterCounts = !isSelect && request.Page <= 1
      ? await GridNamedFilterCounter.ComputeAsync(baseEntityQuery, gridReq, request, queryDeclaration, queryMap, defaultPage.Total, ct)
      : null;

    return new GridPageDto<RowDto>
    {
      Rows = defaultShapedRows,
      BottomRows = defaultBottomRows,
      Forms = await GridRowEnricher.CollectRowFormOverlaysAsync(this, defaultShapedRows, ct),
      NamedFilterCounts = defaultNamedFilterCounts,
      Total = defaultPage.Total,
      Number = defaultPage.Number,
      Size = defaultPage.Size,
      HasMore = defaultPage.HasMore
    };
  }

  private static void NormalizeGridRequest(
    GridRequest? gridReq,
    ListRequest request,
    GridQuery<TEntity, TRow> queryDeclaration,
    bool isSelect,
    bool applyInitialFilters = true)
  {
    if (!string.IsNullOrWhiteSpace(request.Search) && queryDeclaration.PickKeySelector != null)
    {
      request.Search = queryDeclaration.PickKeySelector(request.Search);
    }

    if (request.SearchFields is not { Count: > 0 } && queryDeclaration.SearchFields is { Count: > 0 })
    {
      request.SearchFields = queryDeclaration.SearchFields.ToList();
    }

    if (applyInitialFilters &&
        !isSelect &&
        !(gridReq?.IsDataOnly ?? false) &&
        request.Filter is null &&
        request.ColumnFilter is null &&
        gridReq?.NamedFilters is not { Count: > 0 } &&
        queryDeclaration.InitialFilters.Count > 0)
    {
      request.Filter = queryDeclaration.InitialFilters.Count == 1
        ? queryDeclaration.InitialFilters[0]
        : FilterNode.All([.. queryDeclaration.InitialFilters.Cast<FilterNode>()]);
    }
  }

  public virtual async Task<GridCountDto> GetCountAsync(GridRequest request, CancellationToken ct = default)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    var isSelect = request.ReferenceLabelPaths is { Count: > 0 } || request.IsReferencePicker;
    if (!await EnsureParentScopeAsync(lockParent: false, ct))
    {
      return new GridCountDto { Total = 0 };
    }

    var baseEntityQuery = BuildBaseEntityQuery(request, out _, out var queryDeclaration);
    var query = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration);
    var cols = await CreateColumnsAsync(ct);

    NormalizeGridRequest(request, request, queryDeclaration, isSelect);

    var queryMap = BuildQueryMap(GetType(), queryDeclaration, cols.List, IsSqlServerProvider);

    var total = queryDeclaration.Selector != null
      ? await DataProvider.Create(query.AsNoTracking(), queryMap, queryDeclaration.Selector).Filter(request).CountAsync(ct)
      : await DataProvider.Create(query.AsNoTracking(), queryMap).Filter(request).CountAsync(ct);

    return new GridCountDto { Total = total };
  }

  async Task<object?> IGridService.LoadPickedEntityAsync(GridRequest request, long id, CancellationToken ct)
  {
    ParentService = request.ParentService;
    ParentId = request.ParentId;
    if (!await EnsureParentScopeAsync(lockParent: false, ct))
    {
      return null;
    }

    var baseEntityQuery = BuildBaseEntityQuery(request, out _, out var queryDeclaration);
    var query = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration);
    return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
  }
}
