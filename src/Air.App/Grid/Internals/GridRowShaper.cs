using System.Runtime.CompilerServices;
using Platform.App.Form.Internals;
using Platform.App.Internals;

namespace Platform.App.Grid.Internals;

internal static class GridRowShaper
{
  public static RowDto ShapeDefaultRow<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    TRow row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>? rowFlags = null)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (row is null)
    {
      return new RowDto();
    }

    var shaped = new RowDto
    {
      Cells = new Dictionary<string, object?>(Math.Max(4, columns.Count), StringComparer.Ordinal)
    };

    if (row is IEntity entityRow)
    {
      shaped.Id = entityRow.Id;
    }
    else if (PropertyAccessorCache.GetValue(row, "Id") is { } idVal &&
             FormValidator.TryExtractIntegralOrEnumId(idVal, out var parsedId))
    {
      shaped.Id = parsedId;
    }

    if (PropertyAccessorCache.GetValue(row, "Ver") is { } verVal &&
        FormValidator.TryExtractIntegralOrEnumId(verVal, out var parsedVer))
    {
      shaped.Ver = parsedVer;
    }

    if (rowFlags is { Count: > 0 })
    {
      List<string>? matchedFlags = null;
      foreach (var (flagName, predicate) in rowFlags)
      {
        try
        {
          if (predicate(row))
          {
            matchedFlags ??= [];
            matchedFlags.Add(flagName);
          }
        }
        catch { }
      }

      if (matchedFlags is { Count: > 0 })
      {
        shaped.Flags = matchedFlags;
      }
    }

    if (columns.Count == 0)
    {
      foreach (var prop in PropertyAccessorCache.GetProperties(row.GetType()))
      {
        if (!prop.CanRead ||
            FormValueBinder.IsIgnoredNavigationProperty(prop.PropertyType) ||
            Attribute.IsDefined(prop, typeof(SensitiveAttribute)) ||
            prop.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
        {
          continue;
        }

        if (PropertyAccessorCache.GetValue(row, prop.Name) is { } propVal)
        {
          shaped.Cells[PropertyAccessorCache.ToCamel(prop.Name)] = propVal;
        }
      }

      return shaped;
    }

    var localizer = grid.L;
    var gridType = grid.GetType();
    for (var cIdx = 0; cIdx < columns.Count; cIdx++)
    {
      var column = columns[cIdx];
      var runtime = GridColumnRuntime.TryGet(column);
      if (string.IsNullOrEmpty(column.Path) ||
          column.Cell is GridCellCoverModel { Service.Length: > 0 } && runtime?.FormServiceResolver is null)
      {
        continue;
      }

      var value = runtime?.ValueEvaluator != null
        ? runtime.ValueEvaluator(row)
        : PropertyAccessorCache.GetPathValue(row, column.Path);
      var formattedByCustomFormatter = false;
      if (runtime?.ValueFormatter != null && (!runtime.IsSubqueryExpression || value != null))
      {
        value = runtime.ValueFormatter(value);
        formattedByCustomFormatter = true;
      }

      var allocatedRef = false;

      if (column.Options is { Count: > 0 } options && !formattedByCustomFormatter)
      {
        if (value is not null and not Reference)
        {
          var id = Convert.ToInt64(value, CultureInfo.InvariantCulture);
          var opt = options.FirstOrDefault(o => o.Id == id);
          var name = opt != null ? localizer[opt.Name] : id.ToString(CultureInfo.InvariantCulture);
          value = new Reference { Id = id, Name = name };
          allocatedRef = true;
        }
        else if (value is Reference optRef)
        {
          value = new Reference { Id = optRef.Id, Name = localizer[optRef.Name], Color = optRef.Color };
          allocatedRef = true;
        }
      }
      else if (column.ReferenceService is not null && value is IEntity refEntity)
      {
        string? rawName = null;
        if (column.ReferenceLabelPaths is { Count: > 0 } labelPaths)
        {
          if (labelPaths.Count == 1)
          {
            rawName = PropertyAccessorCache.GetPathValue(value, labelPaths[0])?.ToString();
          }
          else
          {
            List<string>? parts = null;
            for (var i = 0; i < labelPaths.Count; i++)
            {
              var part = PropertyAccessorCache.GetPathValue(value, labelPaths[i])?.ToString();
              if (!string.IsNullOrWhiteSpace(part))
              {
                parts ??= new List<string>(labelPaths.Count);
                parts.Add(part);
              }
            }

            if (parts is { Count: > 0 })
            {
              rawName = string.Join(" — ", parts);
            }
          }
        }

        rawName ??= PropertyAccessorCache.GetReferenceLabel(value)
          ?? refEntity.Id.ToString(CultureInfo.InvariantCulture);
        value = new Reference { Id = refEntity.Id, Name = rawName };
        allocatedRef = true;
      }

      if (runtime?.ColorEvaluator is not null)
      {
        var hue = runtime.ColorEvaluator(row)?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(hue))
        {
          if (value is Reference r)
          {
            if (allocatedRef)
            {
              r.Color = hue;
            }
            else
            {
              value = new Reference { Id = r.Id, Name = r.Name, Color = hue };
            }
          }
          else if (value is Dictionary<string, object?> dictVal)
          {
            dictVal["color"] = hue;
          }
          else
          {
            (shaped.Colors ??= new Dictionary<string, string>(StringComparer.Ordinal))[column.Path] = hue;
          }
        }
      }

      if (runtime?.FormServiceResolver is not null && shaped.Open is null)
      {
        var formType = runtime.FormServiceResolver(row);
        if (formType is not null)
        {
          ModuleCatalog.EnsureModuleServiceAccessAllowed(gridType, formType, $"OpenForm on '{column.Path}'");
          shaped.Open = new OpenTargetDto
          {
            Service = ServicePathResolver.Resolve(formType),
            Id = shaped.Id ?? 0
          };
        }
      }

      if (value is not null)
      {
        PutValue(shaped.Cells, column.Path, value);
      }
    }

    return shaped;
  }

  public static void PutValue(Dictionary<string, object?> target, string path, object? value)
  {
    if (!path.Contains('.'))
    {
      var topKey = PropertyAccessorCache.ToCamel(path);
      if (value is Reference topRef && target.TryGetValue(topKey, out var curTop) && curTop is Dictionary<string, object?> topDict)
      {
        topDict["id"] = topRef.Id;
        topDict["name"] = topRef.Name;
        if (!string.IsNullOrEmpty(topRef.Color))
        {
          topDict["color"] = topRef.Color;
        }
      }
      else
      {
        target[topKey] = value;
      }

      return;
    }

    var segments = PropertyAccessorCache.GetCamelPathSegments(path);
    var current = target;
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var key = segments[i];
      if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> nested)
      {
        nested = new Dictionary<string, object?>(4, StringComparer.Ordinal);
        if (existing is Reference existingRef)
        {
          nested["id"] = existingRef.Id;
          nested["name"] = existingRef.Name;
          if (!string.IsNullOrEmpty(existingRef.Color))
          {
            nested["color"] = existingRef.Color;
          }
        }

        current[key] = nested;
      }

      current = nested;
    }

    var lastKey = segments[^1];
    if (value is Reference valRef && current.TryGetValue(lastKey, out var curLast) && curLast is Dictionary<string, object?> curDict)
    {
      curDict["id"] = valRef.Id;
      curDict["name"] = valRef.Name;
      if (!string.IsNullOrEmpty(valRef.Color))
      {
        curDict["color"] = valRef.Color;
      }
    }
    else
    {
      current[lastKey] = value;
    }
  }

  public static async Task<List<RowDto>> ShapeAndEnrichBatchAsync<TEntity, TRow, TSource>(
    GridService<TEntity, TRow> grid,
    IReadOnlyList<TSource> sourceRows,
    IReadOnlyList<GridColumnDto> activeColumns,
    Columns<TEntity, TRow> declaredColumns,
    GridQuery<TEntity, TRow> queryDeclaration,
    bool includeScreenEnrichments,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
    where TSource : class
  {
    if (sourceRows.Count == 0)
    {
      return [];
    }

    if (queryDeclaration.ComputeAction != null && sourceRows is IReadOnlyList<TRow> typedRows)
    {
      await queryDeclaration.ComputeAction(typedRows, ct);
    }

    var shapedRows = new List<RowDto>(sourceRows.Count);
    foreach (var row in sourceRows)
    {
      shapedRows.Add(grid.ShapeRowInternal((row as TRow)!, activeColumns, queryDeclaration.RowFlags));
    }

    if (queryDeclaration.RowComputeAction != null && sourceRows is IReadOnlyList<TEntity> entityRows)
    {
      await InvokeRowComputeActionAsync(entityRows, shapedRows, activeColumns, queryDeclaration.RowComputeAction, ct);
    }

    await GridRowEnricher.PopulateComputedColumnsAsync(grid, shapedRows, activeColumns, ct);
    await GridRowEnricher.PopulateProjectedColorsAsync(grid, shapedRows, activeColumns, ct);
    if (includeScreenEnrichments)
    {
      await GridRowEnricher.PopulateCoversAsync(grid, shapedRows, activeColumns, ct);
      await GridRowEnricher.PopulateRowAvailabilityAsync(grid, shapedRows, declaredColumns, ct);
    }

    return shapedRows;
  }

  public static async IAsyncEnumerable<RowDto> StreamShapedChunksAsync<TEntity, TRow, TSource>(
    GridService<TEntity, TRow> grid,
    IAsyncEnumerable<IReadOnlyList<TSource>> chunks,
    IReadOnlyList<GridColumnDto> activeColumns,
    Columns<TEntity, TRow> declaredColumns,
    GridQuery<TEntity, TRow> queryDeclaration,
    [EnumeratorCancellation] CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
    where TSource : class
  {
    await foreach (var chunk in chunks.WithCancellation(ct))
    {
      var shapedBatch = await ShapeAndEnrichBatchAsync(
        grid,
        chunk,
        activeColumns,
        declaredColumns,
        queryDeclaration,
        includeScreenEnrichments: false,
        ct);
      foreach (var shaped in shapedBatch)
      {
        yield return shaped;
      }
    }
  }

  public static async Task InvokeRowComputeActionAsync<TEntity>(
    IReadOnlyList<TEntity> entities,
    List<RowDto> shapedRows,
    IReadOnlyList<GridColumnDto> columns,
    Func<IReadOnlyList<GridComputedRow<TEntity>>, CancellationToken, Task> rowComputeAction,
    CancellationToken ct)
    where TEntity : class, IEntity
  {
    var count = Math.Min(entities.Count, shapedRows.Count);
    if (count == 0)
    {
      return;
    }

    var computedRows = new List<GridComputedRow<TEntity>>(count);
    for (var i = 0; i < count; i++)
    {
      computedRows.Add(new GridComputedRow<TEntity>(entities[i], shapedRows[i]));
    }

    await rowComputeAction(computedRows, ct);

    foreach (var col in columns)
    {
      var runtime = GridColumnRuntime.TryGet(col);
      if (runtime?.IsComputed != true || runtime.ValueFormatter == null || string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

      foreach (var row in computedRows)
      {
        if (row[col.Path] is { } val)
        {
          row[col.Path] = runtime.ValueFormatter(val);
        }
      }
    }
  }
}
