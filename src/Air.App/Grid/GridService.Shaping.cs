using System.Runtime.CompilerServices;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected virtual object ShapeRow(TRow row) => row;

  protected virtual object ShapeRow(
    TRow row,
    IReadOnlyList<GridColumnDto> columns,
    IReadOnlyList<(string Name, Func<TRow, bool> Predicate)>? rowFlags = null)
  {
    if (row is null)
    {
      return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    if (columns.Count == 0)
    {
      return ShapeRow(row);
    }

    if (HasCustomShapeRow)
    {
      _ = ShapeRow(row);
    }

    var shaped = new Dictionary<string, object?>(columns.Count + 4, StringComparer.Ordinal);
    if (row is IEntity entityRow)
    {
      shaped["id"] = entityRow.Id;
    }
    else if (PropertyAccessorCache.GetValue(row, "Id") is { } idVal)
    {
      shaped["id"] = idVal;
    }

    if (PropertyAccessorCache.GetValue(row, "Ver") is { } verVal)
    {
      shaped["ver"] = verVal;
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
        shaped["flags"] = matchedFlags;
      }
    }

    for (var cIdx = 0; cIdx < columns.Count; cIdx++)
    {
      var column = columns[cIdx];
      if (string.IsNullOrEmpty(column.Path) ||
          column.Cell is GridCellCoverModel && column.FormServiceResolver is null)
      {
        continue;
      }

      var value = ReadPathValue(row, column.Path);
      var allocatedRef = false;

      if (column.Options is { Count: > 0 } options)
      {
        if (value is not null and not Reference)
        {
          var id = Convert.ToInt64(value, CultureInfo.InvariantCulture);
          var opt = options.FirstOrDefault(o => o.Id == id);
          var name = opt != null ? L[opt.Name] : id.ToString(CultureInfo.InvariantCulture);
          value = new Reference { Id = id, Name = name };
          allocatedRef = true;
        }
        else if (value is Reference optRef)
        {
          value = new Reference { Id = optRef.Id, Name = L[optRef.Name], Color = optRef.Color };
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
            rawName = ReadPathValue(value, labelPaths[0])?.ToString();
          }
          else
          {
            List<string>? parts = null;
            for (var i = 0; i < labelPaths.Count; i++)
            {
              var part = ReadPathValue(value, labelPaths[i])?.ToString();
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
            shaped[$"{column.Path}Color"] = hue;
          }
        }
      }

      if (column.FormServiceResolver is not null && !shaped.ContainsKey("form"))
      {
        var formType = column.FormServiceResolver(row);
        if (formType is not null)
        {
          shaped["form"] = ServicePathResolver.Resolve(formType);
        }
      }

      if (value is not null)
      {
        PutValue(shaped, column.Path, value);
      }
    }

    return shaped;
  }

  private static object? ReadPathValue(object? target, string path) =>
    PropertyAccessorCache.GetPathValue(target, path);

  private static void PutValue(Dictionary<string, object?> target, string path, object? value)
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

  private async IAsyncEnumerable<object> ShapeStreamAsync(
    IAsyncEnumerable<TRow> source,
    IReadOnlyList<GridColumnDto> columns,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in source.WithCancellation(ct))
    {
      yield return ShapeRow(item, columns);
    }
  }

  private async IAsyncEnumerable<object> ShapeEntityStreamAsync(
    IAsyncEnumerable<TEntity> source,
    IReadOnlyList<GridColumnDto> columns,
    [EnumeratorCancellation] CancellationToken ct)
  {
    await foreach (var item in source.WithCancellation(ct))
    {
      yield return ShapeRow((item as TRow)!, columns);
    }
  }

  private static async IAsyncEnumerable<object> ToAsyncEnumerable(IEnumerable<object> source)
  {
    foreach (var item in source)
    {
      yield return item;
    }

    await Task.CompletedTask;
  }
}
