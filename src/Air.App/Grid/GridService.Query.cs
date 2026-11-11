using Platform.App.Grid.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  private IQueryable<TEntity> BuildBaseEntityQuery(
    GridRequest? gridReq,
    out Parent parent,
    out Query queryDeclaration,
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
    var restrictions = new UserRestrictions();
    SetUserRestrictions(restrictions);
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

  private (string? Service, long? ParentId, IReadOnlyList<Form.ChildGridBuilder> Children)? _cachedParentChildren;

  private Form.ChildGridBuilder? ResolveParentChildBuilder()
  {
    var myService = ServicePathResolver.Resolve(GetType());
    return ParentScopeValidator.ResolveChildBuilder(
      ParentService,
      GetParent().BoundParentService,
      ParentId,
      _cachedParentChildren,
      c => c.ServiceType == GetType() || string.Equals(c.ServicePath, myService, StringComparison.OrdinalIgnoreCase));
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
    if (isSelect)
    {
      return query;
    }

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
    var cols = CreateColumns();
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

    var queryMap = BuildQueryMap(GetType(), queryDeclaration, allColumns, IsSqlServerProvider);

    if (queryDeclaration.Selector != null)
    {
      var noTrackingFiltered = filteredQuery.AsNoTracking();
      var dataProvider = DataProvider.Create(noTrackingFiltered, queryMap, queryDeclaration.Selector);
      var page = await dataProvider.GetPageAsync(request, ct);
      if (queryDeclaration.ComputeAction != null && page.Rows.Count > 0)
      {
        await queryDeclaration.ComputeAction(page.Rows, ct);
      }

      var shapedRows = page.Rows.Select(r => ShapeRow(r, cols.List, queryDeclaration.RowFlags)).ToList();
      await PopulateCoversAsync(shapedRows, cols.List, ct);
      await PopulateProjectedColorsAsync(shapedRows, cols.List, ct);
      await PopulateRowAvailabilityAsync(shapedRows, cols, ct);
      var filteredEntities = DataProvider.Create(noTrackingFiltered, queryMap, queryDeclaration.Selector).Filter(request);
      var bottomRows = !isSelect ? await ComputeSumRowsAsync(filteredEntities, queryDeclaration.Selector, cols.List, ct) : null;
      var namedFilterCounts = !isSelect && request.Page <= 1
        ? await ComputeNamedFilterCountsAsync(baseEntityQuery, gridReq, request, queryDeclaration, queryMap, page.Total, ct)
        : null;
      return new GridPageDto<RowDto>
      {
        Rows = shapedRows,
        BottomRows = bottomRows,
        Forms = await CollectRowFormOverlaysAsync(shapedRows, ct),
        NamedFilterCounts = namedFilterCounts,
        Total = page.Total,
        Number = page.Number,
        Size = page.Size,
        HasMore = page.HasMore
      };
    }

    var defaultSelector = CanUseDefaultProjection(filteredQuery, queryDeclaration)
      ? GridProjectionBuilder.TryBuildSelector<TEntity>(cols.List, HasCustomShapeRow)
      : null;

    Page<TEntity> defaultPage;
    if (defaultSelector != null)
    {
      var projectedProvider = DataProvider.Create(filteredQuery.AsNoTracking(), queryMap, defaultSelector);
      defaultPage = await projectedProvider.GetPageAsync(request, ct);
    }
    else
    {
      var queryWithNavs = IncludeColumnNavigations(filteredQuery.AsNoTrackingWithIdentityResolution(), allColumns);
      var defaultDataProvider = DataProvider.Create(queryWithNavs, queryMap);
      defaultPage = await defaultDataProvider.GetPageAsync(request, ct);
    }

    if (queryDeclaration.ComputeAction != null && defaultPage.Rows.Count > 0 && defaultPage.Rows is IReadOnlyList<TRow> typedPageRows)
    {
      await queryDeclaration.ComputeAction(typedPageRows, ct);
    }

    var defaultShapedRows = defaultPage.Rows.Select(r => ShapeRow((r as TRow)!, cols.List, queryDeclaration.RowFlags)).ToList();
    if (queryDeclaration.RowComputeAction != null && defaultPage.Rows.Count > 0)
    {
      await InvokeRowComputeActionAsync(defaultPage.Rows, defaultShapedRows, cols.List, queryDeclaration.RowComputeAction, ct);
    }

    await PopulateComputedColumnsAsync(defaultShapedRows, cols.List, ct);
    await PopulateCoversAsync(defaultShapedRows, cols.List, ct);
    await PopulateRowAvailabilityAsync(defaultShapedRows, cols, ct);
    var sumSourceQuery = DataProvider.Create(filteredQuery.AsNoTracking(), queryMap).Filter(request);
    var defaultBottomRows = !isSelect ? await ComputeSumRowsAsync(sumSourceQuery, null, cols.List, ct) : null;
    var defaultNamedFilterCounts = !isSelect && request.Page <= 1
      ? await ComputeNamedFilterCountsAsync(baseEntityQuery, gridReq, request, queryDeclaration, queryMap, defaultPage.Total, ct)
      : null;

    return new GridPageDto<RowDto>
    {
      Rows = defaultShapedRows,
      BottomRows = defaultBottomRows,
      Forms = await CollectRowFormOverlaysAsync(defaultShapedRows, ct),
      NamedFilterCounts = defaultNamedFilterCounts,
      Total = defaultPage.Total,
      Number = defaultPage.Number,
      Size = defaultPage.Size,
      HasMore = defaultPage.HasMore
    };
  }

  private static async Task<Dictionary<string, int>?> ComputeNamedFilterCountsAsync(
    IQueryable<TEntity> baseEntityQuery,
    GridRequest? gridReq,
    ListRequest request,
    Query queryDeclaration,
    QueryMap<TEntity> queryMap,
    int? currentTotal,
    CancellationToken ct)
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

  private static void NormalizeGridRequest(
    GridRequest? gridReq,
    ListRequest request,
    Query queryDeclaration,
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
    var cols = CreateColumns();

    NormalizeGridRequest(request, request, queryDeclaration, isSelect);

    var queryMap = BuildQueryMap(GetType(), queryDeclaration, cols.List, IsSqlServerProvider);

    var total = queryDeclaration.Selector != null
      ? await DataProvider.Create(query.AsNoTracking(), queryMap, queryDeclaration.Selector).Filter(request).CountAsync(ct)
      : await DataProvider.Create(query.AsNoTracking(), queryMap).Filter(request).CountAsync(ct);

    return new GridCountDto { Total = total };
  }

  private async Task PopulateRowAvailabilityAsync(List<RowDto> shapedRows, Columns columns, CancellationToken ct)
  {
    if (shapedRows.Count == 0)
    {
      return;
    }

    var actions = new Actions();
    await SetActionsAsync(actions, ct);
    var actionOfferGroups = new List<(string Key, IReadOnlyList<Screens.RecordAvailability> Offers)>();
    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      seenKeys.Add(entry.Key);
      if (entry.Offers.Count > 0)
      {
        actionOfferGroups.Add((entry.Key, entry.Offers));
      }
    }

    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, PlatformApp) is Form.IFormService form)
      {
        if (PlatformApp != null)
        {
          form.AttachPlatformApp(PlatformApp);
        }

        foreach (var (key, inheritedOffers) in await form.GetActionOffersAsync(ParentId, ParentService, ct))
        {
          if (seenKeys.Add(key) && inheritedOffers.Count > 0)
          {
            actionOfferGroups.Add((key, inheritedOffers));
          }
        }
      }
    }

    var statusFlow = columns.StatusFlow as Screens.IRecordStatuses<TEntity>;
    if (PlatformApp != null && statusFlow is Flow.StatusFlowService<TEntity> flowSvc)
    {
      flowSvc.AttachPlatformApp(PlatformApp);
    }

    var transitionsWithOffers = statusFlow?.Transitions.Where(t => t.Offers.Count > 0).ToList() ?? [];
    if (transitionsWithOffers.Count == 0 && actionOfferGroups.Count == 0)
    {
      return;
    }

    var rowIds = ExtractRowIds(shapedRows);
    if (rowIds.Count == 0)
    {
      return;
    }

    var offers = new List<(string ActionKey, int ReasonIndex, Expression<Func<TEntity, bool>> Condition)>();
    foreach (var transition in transitionsWithOffers)
    {
      var actionKey = $"status.{transition.Name.ToLowerInvariant()}";
      for (var i = 0; i < transition.Offers.Count; i++)
      {
        if (transition.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
        {
          offers.Add((actionKey, i, cond));
        }
      }
    }

    foreach (var (actionKey, actionOffers) in actionOfferGroups)
    {
      for (var i = 0; i < actionOffers.Count; i++)
      {
        if (actionOffers[i].Condition is Expression<Func<TEntity, bool>> cond)
        {
          offers.Add((actionKey, i, cond));
        }
      }
    }

    if (offers.Count == 0)
    {
      return;
    }

    var evaluated = await OfferBatchEvaluator.EvaluateAsync(db, rowIds, offers, ct);
    foreach (var row in shapedRows)
    {
      if (row.Id is not { } rowId || !evaluated.TryGetValue(rowId, out var rowUnav) || rowUnav.Count == 0)
      {
        continue;
      }

      if (row.Unavailable is null)
      {
        row.Unavailable = rowUnav;
      }
      else
      {
        foreach (var kvp in rowUnav)
        {
          row.Unavailable.TryAdd(kvp.Key, kvp.Value);
        }
      }
    }
  }

  private static readonly ConcurrentDictionary<
    Type,
    Func<IQueryable<TEntity>, IReadOnlyList<long>, LambdaExpression, Func<object?, object?>?, CancellationToken, Task<Dictionary<long, object?>>>> ComputedColumnEvaluators = new();

  private static Func<IQueryable<TEntity>, IReadOnlyList<long>, LambdaExpression, Func<object?, object?>?, CancellationToken, Task<Dictionary<long, object?>>> GetComputedColumnEvaluator(Type propType) =>
    ComputedColumnEvaluators.GetOrAdd(propType, static pt =>
    {
      var method = typeof(ComputedColumnRow<>)
        .MakeGenericType(pt)
        .GetMethod(nameof(ComputedColumnRow<object>.EvaluateAsync), BindingFlags.Public | BindingFlags.Static)!
        .MakeGenericMethod(typeof(TEntity));
      return method.CreateDelegate<Func<IQueryable<TEntity>, IReadOnlyList<long>, LambdaExpression, Func<object?, object?>?, CancellationToken, Task<Dictionary<long, object?>>>>();
    });

  private async Task PopulateComputedColumnsAsync(List<RowDto> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
  {
    if (typeof(TRow) != typeof(TEntity) || shapedRows.Count == 0)
    {
      return;
    }

    var subqueryCols = columns.Where(c => !c.IsHidden && c.IsSubqueryExpression && c.ValueExpression != null && !string.IsNullOrEmpty(c.Path)).ToList();
    if (subqueryCols.Count == 0)
    {
      return;
    }

    var rowIds = ExtractRowIds(shapedRows);
    if (rowIds.Count == 0)
    {
      return;
    }

    var baseQuery = BaseQuery(db).AsNoTracking();
    foreach (var col in subqueryCols)
    {
      var evaluator = GetComputedColumnEvaluator(col.ValueExpression!.ReturnType);
      var valuesById = await evaluator(baseQuery, rowIds, col.ValueExpression, col.ValueFormatter, ct);
      foreach (var row in shapedRows)
      {
        if (row.Id is not { } rowId || !valuesById.TryGetValue(rowId, out var computedVal) || computedVal == null)
        {
          continue;
        }

        if (col.Options is { Count: > 0 } options && col.ValueFormatter == null && computedVal is not Reference)
        {
          var optId = Convert.ToInt64(computedVal, CultureInfo.InvariantCulture);
          var opt = options.FirstOrDefault(o => o.Id == optId);
          var optName = opt != null ? L[opt.Name] : optId.ToString(CultureInfo.InvariantCulture);
          string? hue = null;
          if (row.Colors != null && row.Colors.Remove(col.Path!, out var colorStr))
          {
            hue = colorStr;
            if (row.Colors.Count == 0)
            {
              row.Colors = null;
            }
          }

          computedVal = new Reference { Id = optId, Name = optName, Color = hue };
        }

        PutValue(row.Cells, col.Path!, computedVal);
      }
    }
  }

  private async Task PopulateCoversAsync(List<RowDto> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
  {
    var coverCol = columns.FirstOrDefault(c => !c.IsHidden && c.Cell is AddOns.Covers.Contracts.GridCellCoverModel);
    if (coverCol == null || shapedRows.Count == 0)
    {
      return;
    }

    var coverCell = (AddOns.Covers.Contracts.GridCellCoverModel)coverCol.Cell!;
    var ownerIds = ExtractRowIds(shapedRows);
    if (ownerIds.Count == 0)
    {
      return;
    }

    var fileGridType = ModuleCatalog.Instance.FindServiceType(coverCell.Service);
    if (fileGridType != null && typeof(Files.Internals.IFileCoverSource).IsAssignableFrom(fileGridType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(fileGridType, db, PlatformApp) is Files.Internals.IFileCoverSource coverSource)
        {
          if (coverSource is IGridService coverGrid && !coverGrid.CanView(new GridRequest(), User))
          {
            return;
          }

          var covers = await coverSource.CoversAsync(ownerIds, ct);
          foreach (var row in shapedRows)
          {
            if (row.Id is { } rowId && covers.TryGetValue(rowId, out var coverDto))
            {
              row.Cells[coverCol.Path ?? "idCover"] = coverDto;
            }
          }
        }
      }
      catch
      {
      }
    }
  }

  private async Task PopulateProjectedColorsAsync(List<RowDto> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
  {
    if (typeof(TRow) == typeof(TEntity) || shapedRows.Count == 0)
    {
      return;
    }

    var colorCols = columns.Where(c => c.ColorEvaluator != null && !string.IsNullOrEmpty(c.Path)).ToList();
    if (colorCols.Count == 0)
    {
      return;
    }

    var rowIds = ExtractRowIds(shapedRows);
    if (rowIds.Count == 0)
    {
      return;
    }

    var entityQuery = IncludeColumnNavigations(BaseQuery(db).AsNoTracking(), colorCols)
      .Where(e => rowIds.Contains(e.Id));
    var entitiesById = await entityQuery.ToDictionaryAsync(e => e.Id, ct);

    foreach (var row in shapedRows)
    {
      if (row.Id is not { } rowId || !entitiesById.TryGetValue(rowId, out var entity))
      {
        continue;
      }

      foreach (var col in colorCols)
      {
        var hue = col.ColorEvaluator!(entity)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(hue) || string.IsNullOrEmpty(col.Path))
        {
          continue;
        }

        var val = PropertyAccessorCache.GetPathValue(row.Cells, PropertyAccessorCache.ToCamel(col.Path));
        if (val is Reference r)
        {
          r.Color = hue;
        }
        else if (val is Dictionary<string, object?> dictVal)
        {
          dictVal["color"] = hue;
        }
        else
        {
          (row.Colors ??= new Dictionary<string, string>(StringComparer.Ordinal))[col.Path] = hue;
        }
      }
    }
  }

  private bool CanUseDefaultProjection(IQueryable<TEntity> query, Query queryDeclaration) =>
    typeof(TRow) == typeof(TEntity) &&
    !HasCustomShapeRow &&
    !((queryDeclaration.ComputeAction != null || queryDeclaration.RowComputeAction != null || queryDeclaration.RowFlags.Count > 0) &&
      GridProjectionBuilder.HasIncludeExpressions(query.Expression));

  private static List<long> ExtractRowIds(List<RowDto> shapedRows)
  {
    var ids = new List<long>(shapedRows.Count);
    foreach (var row in shapedRows)
    {
      if (row.Id is { } rowId)
      {
        ids.Add(rowId);
      }
    }

    return ids;
  }

  private static IQueryable<TEntity> IncludeColumnNavigations(IQueryable<TEntity> query, IEnumerable<GridColumnDto> columns)
  {
    var entityType = typeof(TEntity);
    foreach (var col in columns)
    {
      if (string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

      var parts = col.Path.Split('.');
      if (col.ReferenceService != null)
      {
        var actualNavPath = ResolvePropertyPath(entityType, parts);
        if (actualNavPath != null)
        {
          try
          {
            query = query.Include(actualNavPath);
          }
          catch { }
        }
      }
      else if (parts.Length > 1)
      {
        var actualNavPath = ResolvePropertyPath(entityType, parts[..^1]);
        if (actualNavPath != null)
        {
          try
          {
            query = query.Include(actualNavPath);
          }
          catch { }
        }
      }

      if (!string.IsNullOrEmpty(col.ColorNavPath))
      {
        var actualColorNavPath = ResolvePropertyPath(entityType, col.ColorNavPath.Split('.'));
        if (actualColorNavPath != null)
        {
          try
          {
            query = query.Include(actualColorNavPath);
          }
          catch { }
        }
      }

      if (col.ValueExpression != null && !col.IsSubqueryExpression)
      {
        foreach (var navPath in Form.Elements.ExtractNavigationPaths(col.ValueExpression))
        {
          try
          {
            query = query.Include(navPath);
          }
          catch { }
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
      if (prop == null || GridQueryMapBuilder<TEntity, TRow>.IsScalarType(prop.PropertyType))
      {
        return null;
      }

      validProps.Add(prop.Name);
      currentType = prop.PropertyType;
    }

    return validProps.Count > 0 ? string.Join('.', validProps) : null;
  }

  private async Task<Dictionary<string, OverlayDto>?> CollectRowFormOverlaysAsync(List<RowDto> shapedRows, CancellationToken ct = default)
  {
    Dictionary<string, OverlayDto>? forms = null;
    foreach (var row in shapedRows)
    {
      if (row.Open?.Service is { Length: > 0 } formHandle &&
          (forms == null || !forms.ContainsKey(formHandle)))
      {
        if (_cachedFormOverlays.TryGetValue(formHandle, out var cached))
        {
          forms ??= new Dictionary<string, OverlayDto>(StringComparer.Ordinal);
          forms[formHandle] = cached;
          continue;
        }

        var formType = ModuleCatalog.Instance.FindServiceType(formHandle);
        if (formType != null && typeof(Form.IFormService).IsAssignableFrom(formType))
        {
          forms ??= new Dictionary<string, OverlayDto>(StringComparer.Ordinal);
          forms[formHandle] = await ResolveFormOverlayForTypeAsync(formType, formHandle, ct);
        }
      }
    }

    return forms;
  }

  private bool IsSqlServerProvider =>
    db.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

  internal static QueryMap<TEntity> BuildQueryMap(
    Type serviceType,
    Query queryDeclaration,
    IReadOnlyList<GridColumnDto> columns,
    bool useCollationComparison = false) =>
    GridQueryMapBuilder<TEntity, TRow>.Build(serviceType, queryDeclaration, columns, useCollationComparison);
}
