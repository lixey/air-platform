using System.Globalization;
using System.Runtime.CompilerServices;
using Air.App.Internal;
using Air.App.Localization;
using Air.Data.Contracts;
using Air.Data.Entities;

namespace Air.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  protected virtual object ShapeRow(TRow row, IReadOnlyList<GridColumnDto> columns)
  {
    if (row is null || columns.Count == 0) return row!;

    var shaped = new Dictionary<string, object?>(StringComparer.Ordinal);
    if (PropertyAccessorCache.GetValue(row, "Id") is { } idVal)
    {
      shaped["id"] = idVal;
    }
    if (PropertyAccessorCache.GetValue(row, "Ver") is { } verVal)
    {
      shaped["ver"] = verVal;
    }

    foreach (var column in columns)
    {
      if (string.IsNullOrEmpty(column.Path)) continue;

      var value = ReadPathValue(row, column.Path);

      if (column.Options is { Count: > 0 } options)
      {
        if (value is not null and not Reference)
        {
          var id = Convert.ToInt64(value, CultureInfo.InvariantCulture);
          var opt = options.FirstOrDefault(o => o.Id == id);
          var name = opt != null ? L[opt.Name] : id.ToString(CultureInfo.InvariantCulture);
          value = !string.IsNullOrEmpty(opt?.Color)
            ? new Dictionary<string, object?> { ["id"] = id, ["name"] = name, ["color"] = opt.Color }
            : new Reference { Id = id, Name = name };
        }
        else if (value is Reference optRef)
        {
          var opt = options.FirstOrDefault(o => o.Id == optRef.Id);
          var name = L[optRef.Name];
          value = !string.IsNullOrEmpty(opt?.Color)
            ? new Dictionary<string, object?> { ["id"] = optRef.Id, ["name"] = name, ["color"] = opt.Color }
            : new Reference { Id = optRef.Id, Name = name };
        }
      }
      else if (value is Reference existingRef)
      {
        value = new Reference
        {
          Id = existingRef.Id,
          Name = L[existingRef.Name]
        };
      }
      else if (column.ReferenceService is not null && value is IEntity refEntity)
      {
        var rawName = PropertyAccessorCache.GetValue(value, "Name")?.ToString()
          ?? refEntity.Id.ToString(CultureInfo.InvariantCulture);
        value = new Reference { Id = refEntity.Id, Name = L[rawName] };
      }
      else if (value is string strVal && !string.IsNullOrEmpty(strVal) &&
               ((row is IReadOnlyEntity && string.Equals(column.Path, "name", StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(column.Path, "type", StringComparison.OrdinalIgnoreCase)))
      {
        value = L[strVal];
      }

      if (column.ColorEvaluator is not null)
      {
        var hue = column.ColorEvaluator(row);
        if (!string.IsNullOrEmpty(hue))
        {
          if (value is Reference r)
          {
            value = new Dictionary<string, object?>
            {
              ["id"] = r.Id,
              ["name"] = L[r.Name],
              ["color"] = hue
            };
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
          shaped["form"] = Modules.ServicePathResolver.Resolve(formType);
        }
      }

      PutValue(shaped, column.Path.Split('.'), value);
    }

    return shaped;
  }

  private static object? ReadPathValue(object? target, string path) =>
    PropertyAccessorCache.GetPathValue(target, path);

  private static void PutValue(Dictionary<string, object?> target, string[] segments, object? value)
  {
    var current = target;
    for (var i = 0; i < segments.Length - 1; i++)
    {
      var key = ToCamel(segments[i]);
      if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> nested)
      {
        nested = new Dictionary<string, object?>(StringComparer.Ordinal);
        current[key] = nested;
      }
      current = nested;
    }
    current[ToCamel(segments[^1])] = value;
  }

  private static string ToCamel(string str) =>
    string.IsNullOrEmpty(str) ? str : char.ToLowerInvariant(str[0]) + str[1..];

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
