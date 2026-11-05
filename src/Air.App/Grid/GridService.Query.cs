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
      if (parent.FilterFunc != null && parent.MatchesService(ParentService))
      {
        query = parent.FilterFunc(query, ParentId.Value);
      }
      else if (ParentId is > 0 && ResolveParentChildBuilder() is { FilterProvider: { } filterProvider })
      {
        query = (IQueryable<TEntity>)filterProvider(db, ParentId.Value, query);
      }
      else if (parent.FilterFunc != null)
      {
        query = parent.FilterFunc(query, ParentId.Value);
      }
      else if (ParentId is > 0 && !parent.HasRead)
      {
        query = query.Where(_ => false);
      }
    }
    else if (parent.BoundProperty != null &&
             !parent.IsOptional &&
             Feature.ServiceType != null &&
             Feature.ServiceType != GetType() &&
             !(gridReq?.IsReferencePicker ?? false) &&
             gridReq?.ReferenceLabelPaths is not { Count: > 0 })
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
      db,
      PlatformApp,
      ParentService,
      GetParent().BoundParentService,
      ParentId,
      ref _cachedParentChildren,
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

  public virtual async Task<GridPageDto<object>> GetDataAsync(ListRequest request, CancellationToken ct = default)
  {
    var gridReq = request as GridRequest;
    if (gridReq is { ParentId: > 0 })
    {
      ParentService = gridReq.ParentService;
      ParentId = gridReq.ParentId;
      if (!await EnsureParentScopeAsync(lockParent: false, ct))
      {
        return new GridPageDto<object> { Rows = [], Total = 0, Number = request.Page, Size = request.PageSize, HasMore = false };
      }
    }

    var filteredQuery = BuildFilteredEntityQuery(gridReq, out _, out var queryDeclaration);
    var cols = CreateColumns();
    var allColumns = cols.List.ToList();

    var isReferenceSelect = gridReq?.ReferenceLabelPaths is { Count: > 0 };
    var isSelect = isReferenceSelect || (gridReq?.IsReferencePicker ?? false);
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

    var queryMap = BuildQueryMap(queryDeclaration, cols.List);

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
      return new GridPageDto<object>
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

    return new GridPageDto<object>
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
    if (request.ParentId is > 0)
    {
      ParentService = request.ParentService;
      ParentId = request.ParentId;
      if (!await EnsureParentScopeAsync(lockParent: false, ct))
      {
        return new GridCountDto { Total = 0 };
      }
    }

    var baseEntityQuery = BuildBaseEntityQuery(request, out _, out var queryDeclaration);
    var query = ApplyActiveNamedFilters(baseEntityQuery, request, queryDeclaration);
    var cols = CreateColumns();
    var isSelect = request.ReferenceLabelPaths is { Count: > 0 } || request.IsReferencePicker;

    NormalizeGridRequest(request, request, queryDeclaration, isSelect);

    var queryMap = BuildQueryMap(queryDeclaration, cols.List);

    var total = queryDeclaration.Selector != null
      ? await DataProvider.Create(query.AsNoTracking(), queryMap, queryDeclaration.Selector).Filter(request).CountAsync(ct)
      : await DataProvider.Create(query.AsNoTracking(), queryMap).Filter(request).CountAsync(ct);

    return new GridCountDto { Total = total };
  }

  private async Task PopulateRowAvailabilityAsync(List<object> shapedRows, Columns columns, CancellationToken ct)
  {
    if (shapedRows.Count == 0)
    {
      return;
    }

    var nav = GetNav();
    var actionsWithOffers = nav.Actions.Where(a => a.Offers.Count > 0).ToList();
    var statusFlow = columns.StatusFlow as Screens.IRecordStatuses<TEntity>;
    var transitionsWithOffers = statusFlow?.Transitions.Where(t => t.Offers.Count > 0).ToList() ?? [];
    if (transitionsWithOffers.Count == 0 && actionsWithOffers.Count == 0)
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

    foreach (var action in actionsWithOffers)
    {
      var actionKey = action.Model.Key;
      for (var i = 0; i < action.Offers.Count; i++)
      {
        if (action.Offers[i].Condition is Expression<Func<TEntity, bool>> cond)
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
    foreach (var rowObj in shapedRows)
    {
      if (rowObj is not Dictionary<string, object?> dict || !dict.TryGetValue("id", out var idVal) || idVal == null)
      {
        continue;
      }

      if (!long.TryParse(idVal.ToString(), out var parsedId) || !evaluated.TryGetValue(parsedId, out var rowUnav) || rowUnav.Count == 0)
      {
        continue;
      }

      if (!dict.TryGetValue("unavailable", out var unavObj) || unavObj is not Dictionary<string, int> unav)
      {
        dict["unavailable"] = rowUnav;
      }
      else
      {
        foreach (var kvp in rowUnav)
        {
          unav.TryAdd(kvp.Key, kvp.Value);
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

  private async Task PopulateComputedColumnsAsync(List<object> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
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
      foreach (var rowObj in shapedRows)
      {
        if (rowObj is not Dictionary<string, object?> dict || !dict.TryGetValue("id", out var idVal) || idVal == null)
        {
          continue;
        }

        if (!long.TryParse(idVal.ToString(), out var parsedId) || !valuesById.TryGetValue(parsedId, out var computedVal) || computedVal == null)
        {
          continue;
        }

        if (col.Options is { Count: > 0 } options && computedVal is not Reference)
        {
          var optId = Convert.ToInt64(computedVal, CultureInfo.InvariantCulture);
          var opt = options.FirstOrDefault(o => o.Id == optId);
          var optName = opt != null ? L[opt.Name] : optId.ToString(CultureInfo.InvariantCulture);
          string? hue = null;
          if (dict.Remove($"{col.Path}Color", out var colorObj))
          {
            hue = colorObj?.ToString();
          }

          computedVal = new Reference { Id = optId, Name = optName, Color = hue };
        }

        PutValue(dict, col.Path!, computedVal);
      }
    }
  }

  private async Task PopulateCoversAsync(List<object> shapedRows, IReadOnlyList<GridColumnDto> columns, CancellationToken ct)
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

    foreach (var rowObj in shapedRows)
    {
      if (rowObj is not Dictionary<string, object?> dict || !dict.TryGetValue("id", out var idVal) || idVal == null)
      {
        continue;
      }

      if (!long.TryParse(idVal.ToString(), out var parsedId) || !entitiesById.TryGetValue(parsedId, out var entity))
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

        var val = PropertyAccessorCache.GetPathValue(dict, PropertyAccessorCache.ToCamel(col.Path));
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
          PutValue(dict, $"{col.Path}Color", hue);
        }
      }
    }
  }

  private bool CanUseDefaultProjection(IQueryable<TEntity> query, Query queryDeclaration) =>
    typeof(TRow) == typeof(TEntity) &&
    !HasCustomShapeRow &&
    !((queryDeclaration.ComputeAction != null || queryDeclaration.RowComputeAction != null || queryDeclaration.RowFlags.Count > 0) &&
      GridProjectionBuilder.HasIncludeExpressions(query.Expression));

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
      if (prop == null || IsScalarType(prop.PropertyType))
      {
        return null;
      }

      validProps.Add(prop.Name);
      currentType = prop.PropertyType;
    }

    return validProps.Count > 0 ? string.Join('.', validProps) : null;
  }

  private Dictionary<string, OverlayDto>? CollectRowFormOverlays(List<object> shapedRows)
  {
    Dictionary<string, OverlayDto>? forms = null;
    foreach (var rowObj in shapedRows)
    {
      if (rowObj is Dictionary<string, object?> dict &&
          dict.TryGetValue("form", out var formObj) &&
          formObj is string formHandle &&
          !string.IsNullOrEmpty(formHandle) &&
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
          forms[formHandle] = ResolveFormOverlayForType(formType, formHandle);
        }
      }
    }

    return forms;
  }

  private static readonly ConcurrentDictionary<string, QueryMap<TEntity>> CachedQueryMaps = new(StringComparer.Ordinal);

  internal static QueryMap<TEntity> BuildQueryMap(Query queryDeclaration, IReadOnlyList<GridColumnDto> columns)
  {
    if (queryDeclaration.Selector is null)
    {
      var sb = new StringBuilder(columns.Count * 24 + 32);
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
          .Append(col.IsHidden ? '1' : '0').Append(';');
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

        return CachedQueryMaps.GetOrAdd(sb.ToString(), _ => BuildQueryMapCore(queryDeclaration, columns));
      }
    }

    return BuildQueryMapCore(queryDeclaration, columns);
  }

  private static QueryMap<TEntity> BuildQueryMapCore(Query queryDeclaration, IReadOnlyList<GridColumnDto> columns)
  {
    var builder = queryDeclaration.Selector is not null
      ? QueryMap.FromSelector(queryDeclaration.Selector)
      : QueryMap.For<TEntity>();

    builder.AddEntityProperties(searchable: false);

    var entityParam = Expression.Parameter(typeof(TEntity), "e");
    var hasExplicitSearchFields = queryDeclaration.SearchFields is { Count: > 0 };

    foreach (var col in columns)
    {
      if (string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

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

      if (col.ValueExpression is not null)
      {
        builder.Field(col.Path, col.ValueExpression);
        if (!hasExplicitSearchFields && !col.IsHidden && col.ValueExpression.ReturnType == typeof(string))
        {
          builder.Configure(col.Path, f => f.Searchable());
        }

        continue;
      }

      var isRef = col.ReferenceService != null || col.Cell is GridCellReferenceModel || typeof(Reference).IsAssignableFrom(col.MemberType);
      if (isRef)
      {
        var primaryLabelProp = col.ReferenceLabelPaths is { Count: > 0 } ? col.ReferenceLabelPaths[0] : null;
        var nameExpr = (primaryLabelProp != null
            ? builder.FindExpression($"{col.Path}.{primaryLabelProp}") ?? BuildEntityMemberExpression(entityParam, $"{col.Path}.{primaryLabelProp}", preferNameOnObject: true)
            : null)
          ?? builder.FindExpression(col.Path + ".Name")
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

      var existingExpr = builder.FindExpression(col.Path);
      if (existingExpr is null)
      {
        var expr = BuildEntityMemberExpression(entityParam, col.Path, preferNameOnObject: true);
        if (expr is not null)
        {
          builder.Field(col.Path, expr);
        }
      }
      else if (!hasExplicitSearchFields && !col.IsHidden && existingExpr.ReturnType == typeof(string))
      {
        builder.Configure(col.Path, f => f.Searchable());
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
        else if (BuildEntityMemberExpression(entityParam, sf, preferNameOnObject: true) is { } searchExpr)
        {
          builder.Field(sf, searchExpr, f => f.Searchable());
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
