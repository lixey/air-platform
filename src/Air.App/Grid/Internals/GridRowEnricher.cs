using Platform.App.Files.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridRowEnricher
{
  private static readonly ConcurrentDictionary<(Type EntityType, Type PropType), Delegate> ComputedColumnEvaluators = new();

  private static Func<IQueryable<TEntity>, IReadOnlyList<long>, LambdaExpression, Func<object?, object?>?, CancellationToken, Task<Dictionary<long, object?>>> GetComputedColumnEvaluator<TEntity>(Type propType)
    where TEntity : class, IEntity =>
    (Func<IQueryable<TEntity>, IReadOnlyList<long>, LambdaExpression, Func<object?, object?>?, CancellationToken, Task<Dictionary<long, object?>>>)
    ComputedColumnEvaluators.GetOrAdd((typeof(TEntity), propType), static key =>
    {
      var method = typeof(ComputedColumnRow<>)
        .MakeGenericType(key.PropType)
        .GetMethod(nameof(ComputedColumnRow<object>.EvaluateAsync), BindingFlags.Public | BindingFlags.Static)!
        .MakeGenericMethod(key.EntityType);
      return method.CreateDelegate<Func<IQueryable<TEntity>, IReadOnlyList<long>, LambdaExpression, Func<object?, object?>?, CancellationToken, Task<Dictionary<long, object?>>>>();
    });

  public static async Task PopulateRowAvailabilityAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    List<RowDto> shapedRows,
    Columns<TEntity, TRow> columns,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (shapedRows.Count == 0)
    {
      return;
    }

    var actions = await grid.BuildActionsInternalAsync(ct);
    var actionOfferGroups = new List<(string Key, IReadOnlyList<RecordAvailability> Offers)>();
    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in actions.Entries)
    {
      seenKeys.Add(entry.Key);
      if (entry.Offers.Count > 0)
      {
        actionOfferGroups.Add((entry.Key, entry.Offers));
      }
    }

    var db = grid.DbContext;
    foreach (var formType in actions.InheritedFormServiceTypes)
    {
      if (ModuleCatalog.Instance.CreateServiceInstance(formType, db, grid.PlatformApp) is IFormService form)
      {
        foreach (var (key, inheritedOffers) in await form.GetActionOffersAsync(grid.ParentId, grid.ParentService, ct))
        {
          if (seenKeys.Add(key) && inheritedOffers.Count > 0)
          {
            actionOfferGroups.Add((key, inheritedOffers));
          }
        }
      }
    }

    var statusFlow = columns.StatusFlow as IRecordStatuses<TEntity>;
    (statusFlow as IPlatformService)?.BindContext(grid.PlatformApp);

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

  public static async Task PopulateComputedColumnsAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    List<RowDto> shapedRows,
    IReadOnlyList<GridColumnDto> columns,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
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

    var baseQuery = grid.BaseQueryInternal().AsNoTracking();
    foreach (var col in subqueryCols)
    {
      var evaluator = GetComputedColumnEvaluator<TEntity>(col.ValueExpression!.ReturnType);
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
          var optName = opt != null ? grid.L[opt.Name] : optId.ToString(CultureInfo.InvariantCulture);
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

        GridRowShaper.PutValue(row.Cells, col.Path!, computedVal);
      }
    }
  }

  public static async Task PopulateCoversAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    List<RowDto> shapedRows,
    IReadOnlyList<GridColumnDto> columns,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    var coverCol = columns.FirstOrDefault(c => !c.IsHidden && c.Cell is GridCellCoverModel);
    if (coverCol == null || shapedRows.Count == 0)
    {
      return;
    }

    var coverCell = (GridCellCoverModel)coverCol.Cell!;
    var ownerIds = ExtractRowIds(shapedRows);
    if (ownerIds.Count == 0)
    {
      return;
    }

    var fileGridType = ModuleCatalog.Instance.FindServiceType(coverCell.Service);
    if (fileGridType != null && typeof(IFileCoverSource).IsAssignableFrom(fileGridType))
    {
      try
      {
        if (ModuleCatalog.Instance.CreateServiceInstance(fileGridType, grid.DbContext, grid.PlatformApp) is IFileCoverSource coverSource)
        {
          if (coverSource is IGridService coverGrid && !coverGrid.CanView(new GridRequest(), grid.User))
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

  public static async Task PopulateProjectedColorsAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    List<RowDto> shapedRows,
    IReadOnlyList<GridColumnDto> columns,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
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

    var entityQuery = IncludeColumnNavigations<TEntity, TRow>(grid.BaseQueryInternal().AsNoTracking(), colorCols)
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

  public static bool CanUseDefaultProjection<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IQueryable<TEntity> query,
    GridQuery<TEntity, TRow> queryDeclaration)
    where TEntity : class, IEntity, new()
    where TRow : class =>
    typeof(TRow) == typeof(TEntity) &&
    !grid.HasCustomShapeRow &&
    !((queryDeclaration.ComputeAction != null || queryDeclaration.RowComputeAction != null || queryDeclaration.RowFlags.Count > 0) &&
      GridProjectionBuilder.HasIncludeExpressions(query.Expression));

  public static List<long> ExtractRowIds(List<RowDto> shapedRows)
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

  public static IQueryable<TEntity> IncludeColumnNavigations<TEntity, TRow>(
    IQueryable<TEntity> query,
    IEnumerable<GridColumnDto> columns)
    where TEntity : class, IEntity, new()
    where TRow : class
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
        var actualNavPath = ResolvePropertyPath<TEntity, TRow>(entityType, parts);
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
        var actualNavPath = ResolvePropertyPath<TEntity, TRow>(entityType, parts[..^1]);
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
        var actualColorNavPath = ResolvePropertyPath<TEntity, TRow>(entityType, col.ColorNavPath.Split('.'));
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

  private static string? ResolvePropertyPath<TEntity, TRow>(Type rootType, string[] segments)
    where TEntity : class, IEntity, new()
    where TRow : class
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

  public static async Task<Dictionary<string, OverlayDto>?> CollectRowFormOverlaysAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    List<RowDto> shapedRows,
    CancellationToken ct = default)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    Dictionary<string, OverlayDto>? forms = null;
    foreach (var row in shapedRows)
    {
      if (row.Open?.Service is { Length: > 0 } formHandle &&
          (forms == null || !forms.ContainsKey(formHandle)))
      {
        if (grid.State.CachedFormOverlays.TryGetValue(formHandle, out var cached))
        {
          forms ??= new Dictionary<string, OverlayDto>(StringComparer.Ordinal);
          forms[formHandle] = cached;
          continue;
        }

        var formType = ModuleCatalog.Instance.FindServiceType(formHandle);
        if (formType != null && typeof(IFormService).IsAssignableFrom(formType))
        {
          forms ??= new Dictionary<string, OverlayDto>(StringComparer.Ordinal);
          forms[formHandle] = await grid.ResolveFormOverlayForTypeAsync(formType, formHandle, ct);
        }
      }
    }

    return forms;
  }
}
