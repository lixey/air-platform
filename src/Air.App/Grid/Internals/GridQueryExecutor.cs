namespace Platform.App.Grid.Internals;

internal static class GridQueryExecutor
{
  public static IQueryable<TEntity> BuildBaseEntityQuery<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridRequest? gridReq,
    out GridService<TEntity, TRow>.Parent parent,
    out GridService<TEntity, TRow>.Query queryDeclaration,
    bool preserveParent = false)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    parent = grid.GetParentDeclaration();

    if (gridReq != null)
    {
      grid.ParentService = gridReq.ParentService;
      grid.ParentId = gridReq.ParentId;
    }
    else if (!preserveParent)
    {
      grid.ParentService = null;
      grid.ParentId = null;
    }

    queryDeclaration = grid.GetQueryDeclaration();

    var db = grid.DbContext;
    var query = grid.InvokeBaseQuery(db);
    var restrictions = new GridService<TEntity, TRow>.UserRestrictions();
    grid.InvokeSetUserRestrictions(restrictions);
    foreach (var restriction in restrictions.Filters)
    {
      query = query.Where(restriction);
    }

    foreach (var filter in queryDeclaration.Filters)
    {
      query = query.Where(filter);
    }

    if (grid.ParentId.HasValue)
    {
      if (grid.ParentId.Value <= 0 || !parent.MatchesService(grid.ParentService))
      {
        query = query.Where(_ => false);
      }
      else if (parent.FilterFunc != null)
      {
        query = parent.FilterFunc(query, grid.ParentId.Value);
      }
      else if (grid.ResolveParentChildBuilder() is { FilterProvider: { } filterProvider })
      {
        query = (IQueryable<TEntity>)filterProvider(db, grid.ParentId.Value, query);
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

  public static IQueryable<TEntity> ApplyActiveNamedFilters<TEntity, TRow>(
    IQueryable<TEntity> query,
    GridRequest? gridReq,
    GridService<TEntity, TRow>.Query queryDeclaration,
    GridNamedFilterItem<TEntity>? overrideToggle = null,
    GridNamedFilterGroup<TEntity>? overrideGroup = null,
    GridNamedFilterItem<TEntity>? overrideGroupItem = null)
    where TEntity : class, IEntity, new()
    where TRow : class
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

  public static IQueryable<TEntity> BuildFilteredEntityQuery<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridRequest? gridReq,
    out GridService<TEntity, TRow>.Parent parent,
    out GridService<TEntity, TRow>.Query queryDeclaration)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var query = BuildBaseEntityQuery(grid, gridReq, out parent, out queryDeclaration);
    return ApplyActiveNamedFilters(query, gridReq, queryDeclaration);
  }

  public static async Task<GridPageDto<RowDto>> GetDataAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    ListRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var gridReq = request as GridRequest;
    grid.ParentService = gridReq?.ParentService;
    grid.ParentId = gridReq?.ParentId;
    var isReferenceSelect = gridReq?.ReferenceLabelPaths is { Count: > 0 };
    var isSelect = isReferenceSelect || (gridReq?.IsReferencePicker ?? false);
    if (!await grid.EnsureParentScopeInternalAsync(lockParent: false, ct))
    {
      return new GridPageDto<RowDto> { Rows = [], Total = 0, Number = request.Page, Size = request.PageSize, HasMore = false };
    }

    var baseEntityQuery = BuildBaseEntityQuery(grid, gridReq, out _, out var queryDeclaration);
    var filteredQuery = ApplyActiveNamedFilters(baseEntityQuery, gridReq, queryDeclaration);
    var cols = grid.CreateColumnsDeclaration();
    var allColumns = cols.List.ToList();

    if (isReferenceSelect)
    {
      var labelPaths = gridReq!.ReferenceLabelPaths!;
      var patchColumns = gridReq.PatchSourceColumns ?? [];
      var preserved = new HashSet<string>(labelPaths, StringComparer.OrdinalIgnoreCase);
      foreach (var p in patchColumns)
      {
        preserved.Add(p);
      }

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

    NormalizeGridRequest(gridReq, request, queryDeclaration, isSelect);

    var queryMap = GridService<TEntity, TRow>.BuildQueryMap(grid.GetType(), queryDeclaration, allColumns, grid.IsSqlServerProvider);

    if (queryDeclaration.Selector != null)
    {
      var noTrackingFiltered = filteredQuery.AsNoTracking();
      var dataProvider = DataProvider.Create(noTrackingFiltered, queryMap, queryDeclaration.Selector);
      var page = await dataProvider.GetPageAsync(request, ct);
      if (queryDeclaration.ComputeAction != null && page.Rows.Count > 0)
      {
        await queryDeclaration.ComputeAction(page.Rows, ct);
      }

      var shapedRows = page.Rows.Select(r => grid.InvokeShapeRow(r, cols.List, queryDeclaration.RowFlags)).ToList();
      await GridRowEnricher.PopulateCoversAsync(grid, shapedRows, cols.List, ct);
      await GridRowEnricher.PopulateProjectedColorsAsync(grid, shapedRows, cols.List, ct);
      await GridRowEnricher.PopulateRowAvailabilityAsync(grid, shapedRows, cols, ct);
      var filteredEntities = DataProvider.Create(noTrackingFiltered, queryMap, queryDeclaration.Selector).Filter(request);
      var bottomRows = !isSelect ? await GridSummaryCalculator.ComputeSumRowsAsync(grid.DbContext, filteredEntities, queryDeclaration.Selector, cols.List, ct) : null;
      var namedFilterCounts = !isSelect && request.Page <= 1
        ? await ComputeNamedFilterCountsAsync(baseEntityQuery, gridReq, request, queryDeclaration, queryMap, page.Total, ct)
        : null;
      return new GridPageDto<RowDto>
      {
        Rows = shapedRows,
        BottomRows = bottomRows,
        Forms = await GridRowEnricher.CollectRowFormOverlaysAsync(grid, shapedRows, ct),
        NamedFilterCounts = namedFilterCounts,
        Total = page.Total,
        Number = page.Number,
        Size = page.Size,
        HasMore = page.HasMore
      };
    }

    var defaultSelector = GridRowEnricher.CanUseDefaultProjection(grid, filteredQuery, queryDeclaration)
      ? GridProjectionBuilder.TryBuildSelector<TEntity>(cols.List, grid.HasCustomShapeRow)
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

    if (queryDeclaration.ComputeAction != null && defaultPage.Rows.Count > 0 && defaultPage.Rows is IReadOnlyList<TRow> typedPageRows)
    {
      await queryDeclaration.ComputeAction(typedPageRows, ct);
    }

    var defaultShapedRows = defaultPage.Rows.Select(r => grid.InvokeShapeRow((r as TRow)!, cols.List, queryDeclaration.RowFlags)).ToList();
    if (queryDeclaration.RowComputeAction != null && defaultPage.Rows.Count > 0)
    {
      await GridRowShaper.InvokeRowComputeActionAsync(defaultPage.Rows, defaultShapedRows, cols.List, queryDeclaration.RowComputeAction, ct);
    }

    await GridRowEnricher.PopulateComputedColumnsAsync(grid, defaultShapedRows, cols.List, ct);
    await GridRowEnricher.PopulateCoversAsync(grid, defaultShapedRows, cols.List, ct);
    await GridRowEnricher.PopulateRowAvailabilityAsync(grid, defaultShapedRows, cols, ct);
    var sumSourceQuery = DataProvider.Create(filteredQuery.AsNoTracking(), queryMap).Filter(request);
    var defaultBottomRows = !isSelect ? await GridSummaryCalculator.ComputeSumRowsAsync<TEntity, TRow>(grid.DbContext, sumSourceQuery, null, cols.List, ct) : null;
    var defaultNamedFilterCounts = !isSelect && request.Page <= 1
      ? await ComputeNamedFilterCountsAsync(baseEntityQuery, gridReq, request, queryDeclaration, queryMap, defaultPage.Total, ct)
      : null;

    return new GridPageDto<RowDto>
    {
      Rows = defaultShapedRows,
      BottomRows = defaultBottomRows,
      Forms = await GridRowEnricher.CollectRowFormOverlaysAsync(grid, defaultShapedRows, ct),
      NamedFilterCounts = defaultNamedFilterCounts,
      Total = defaultPage.Total,
      Number = defaultPage.Number,
      Size = defaultPage.Size,
      HasMore = defaultPage.HasMore
    };
  }

  public static async Task<Dictionary<string, int>?> ComputeNamedFilterCountsAsync<TEntity, TRow>(
    IQueryable<TEntity> baseEntityQuery,
    GridRequest? gridReq,
    ListRequest request,
    GridService<TEntity, TRow>.Query queryDeclaration,
    QueryMap<TEntity> queryMap,
    int? currentTotal,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var hasCountedGroup = queryDeclaration.NamedFilterGroups.Any(g => g.Items.Any(i => i.IsCountShown));
    var hasCountedToggle = queryDeclaration.NamedFilters.Any(i => i.IsCountShown);
    if (!hasCountedGroup && !hasCountedToggle)
    {
      return null;
    }

    var nfs = gridReq?.NamedFilters ?? [];
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach (var group in queryDeclaration.NamedFilterGroups)
    {
      if (!group.Items.Any(i => i.IsCountShown))
      {
        continue;
      }

      var reqKey = nfs.FirstOrDefault(k => group.Items.Any(i => string.Equals(i.Key, k, StringComparison.OrdinalIgnoreCase)));
      var chosen = reqKey != null
        ? group.Items.FirstOrDefault(i => string.Equals(i.Key, reqKey, StringComparison.OrdinalIgnoreCase))
        : group.Items.FirstOrDefault(i => i.IsDefault);

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

        var itemQuery = ApplyActiveNamedFilters(
          baseEntityQuery,
          gridReq,
          queryDeclaration,
          overrideGroup: group,
          overrideGroupItem: item).AsNoTracking();
        counts[item.Key] = queryDeclaration.Selector != null
          ? await DataProvider.Create(itemQuery, queryMap, queryDeclaration.Selector).CountAsync(request, ct)
          : await DataProvider.Create(itemQuery, queryMap).CountAsync(request, ct);
      }
    }

    foreach (var nf in queryDeclaration.NamedFilters)
    {
      if (!nf.IsCountShown)
      {
        continue;
      }

      var isCurrentlyOn = nfs.Any(k => string.Equals(k, nf.Key, StringComparison.OrdinalIgnoreCase));
      if (isCurrentlyOn && currentTotal.HasValue)
      {
        counts[nf.Key] = currentTotal.Value;
        continue;
      }

      var toggleQuery = ApplyActiveNamedFilters(
        baseEntityQuery,
        gridReq,
        queryDeclaration,
        overrideToggle: nf).AsNoTracking();
      counts[nf.Key] = queryDeclaration.Selector != null
        ? await DataProvider.Create(toggleQuery, queryMap, queryDeclaration.Selector).CountAsync(request, ct)
        : await DataProvider.Create(toggleQuery, queryMap).CountAsync(request, ct);
    }

    return counts.Count > 0 ? counts : null;
  }

  public static void NormalizeGridRequest<TEntity, TRow>(
    GridRequest? gridReq,
    ListRequest request,
    GridService<TEntity, TRow>.Query queryDeclaration,
    bool isSelect,
    bool applyInitialFilters = true)
    where TEntity : class, IEntity, new()
    where TRow : class
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

  public static async Task<GridCountDto> GetCountAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    GridRequest request,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    grid.ParentService = request.ParentService;
    grid.ParentId = request.ParentId;
    var isSelect = request.ReferenceLabelPaths is { Count: > 0 } || request.IsReferencePicker;
    if (!await grid.EnsureParentScopeInternalAsync(lockParent: false, ct))
    {
      return new GridCountDto { Total = 0 };
    }

    var baseEntityQuery = BuildBaseEntityQuery(grid, request, out _, out var queryDeclaration);
    var query = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration);
    var cols = grid.CreateColumnsDeclaration();

    NormalizeGridRequest(request, request, queryDeclaration, isSelect);

    var queryMap = GridService<TEntity, TRow>.BuildQueryMap(grid.GetType(), queryDeclaration, cols.List, grid.IsSqlServerProvider);

    var total = queryDeclaration.Selector != null
      ? await DataProvider.Create(query.AsNoTracking(), queryMap, queryDeclaration.Selector).Filter(request).CountAsync(ct)
      : await DataProvider.Create(query.AsNoTracking(), queryMap).Filter(request).CountAsync(ct);

    return new GridCountDto { Total = total };
  }
}
