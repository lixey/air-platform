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
      if (string.IsNullOrEmpty(column.Path) ||
          column.Cell is GridCellCoverModel { Service.Length: > 0 } && column.FormServiceResolver is null)
      {
        continue;
      }

      var value = column.ValueEvaluator != null
        ? column.ValueEvaluator(row)
        : PropertyAccessorCache.GetPathValue(row, column.Path);
      var formattedByCustomFormatter = false;
      if (column.ValueFormatter != null && (!column.IsSubqueryExpression || value != null))
      {
        value = column.ValueFormatter(value);
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

      if (column.ColorEvaluator is not null)
      {
        var hue = column.ColorEvaluator(row)?.ToLowerInvariant();
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

      if (column.FormServiceResolver is not null && shaped.Open is null)
      {
        var formType = column.FormServiceResolver(row);
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

  public static async IAsyncEnumerable<RowDto> ShapeStreamAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IAsyncEnumerable<TRow> source,
    IReadOnlyList<GridColumnDto> columns,
    [EnumeratorCancellation] CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    await foreach (var item in source.WithCancellation(ct))
    {
      yield return grid.InvokeShapeRow(item, columns);
    }
  }

  public static async IAsyncEnumerable<RowDto> ShapeEntityStreamAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IAsyncEnumerable<TEntity> source,
    IReadOnlyList<GridColumnDto> columns,
    [EnumeratorCancellation] CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    await foreach (var item in source.WithCancellation(ct))
    {
      yield return grid.InvokeShapeRow((item as TRow)!, columns);
    }
  }

  public static async IAsyncEnumerable<RowDto> ShapeEntityChunksAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IAsyncEnumerable<IReadOnlyList<TEntity>> chunks,
    IReadOnlyList<GridColumnDto> columns,
    Func<IReadOnlyList<TRow>, CancellationToken, Task>? computeAction,
    Func<IReadOnlyList<GridComputedRow<TEntity>>, CancellationToken, Task>? rowComputeAction,
    bool hasSubqueryCols,
    [EnumeratorCancellation] CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    await foreach (var chunk in chunks.WithCancellation(ct))
    {
      foreach (var shaped in await ShapeEntityBatchAsync(grid, chunk, columns, computeAction, rowComputeAction, hasSubqueryCols, ct))
      {
        yield return shaped;
      }
    }
  }

  private static async Task<List<RowDto>> ShapeEntityBatchAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IReadOnlyList<TEntity> entities,
    IReadOnlyList<GridColumnDto> columns,
    Func<IReadOnlyList<TRow>, CancellationToken, Task>? computeAction,
    Func<IReadOnlyList<GridComputedRow<TEntity>>, CancellationToken, Task>? rowComputeAction,
    bool hasSubqueryCols,
    CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    if (computeAction != null && entities is IReadOnlyList<TRow> typedEntities)
    {
      await computeAction(typedEntities, ct);
    }

    var batch = new List<RowDto>(entities.Count);
    foreach (var entity in entities)
    {
      batch.Add(grid.InvokeShapeRow((entity as TRow)!, columns));
    }

    if (rowComputeAction != null)
    {
      await InvokeRowComputeActionAsync(entities, batch, columns, rowComputeAction, ct);
    }

    if (hasSubqueryCols)
    {
      await GridRowEnricher.PopulateComputedColumnsAsync(grid, batch, columns, ct);
    }

    return batch;
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
      if (!col.IsComputed || col.ValueFormatter == null || string.IsNullOrEmpty(col.Path))
      {
        continue;
      }

      foreach (var row in computedRows)
      {
        if (row[col.Path] is { } val)
        {
          row[col.Path] = col.ValueFormatter(val);
        }
      }
    }
  }

  public static async IAsyncEnumerable<RowDto> ShapeComputedChunksAsync<TEntity, TRow>(
    GridService<TEntity, TRow> grid,
    IAsyncEnumerable<IReadOnlyList<TRow>> chunks,
    Func<IReadOnlyList<TRow>, CancellationToken, Task> computeAction,
    IReadOnlyList<GridColumnDto> columns,
    [EnumeratorCancellation] CancellationToken ct)
    where TEntity : class, IEntity, new()
    where TRow : class
  {
    await foreach (var batch in chunks.WithCancellation(ct))
    {
      await computeAction(batch, ct);
      foreach (var row in batch)
      {
        yield return grid.InvokeShapeRow(row, columns);
      }
    }
  }
}
