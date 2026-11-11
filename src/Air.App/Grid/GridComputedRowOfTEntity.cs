using Platform.App.Internals;

namespace Platform.App.Grid;

public readonly struct GridComputedRow<TEntity>(TEntity record, RowDto row)
  where TEntity : class
{
  /// <summary>
  /// The projected entity instance for this row (includes all scalar properties of <typeparamref name="TEntity"/>
  /// and navigation properties referenced by declared grid columns).
  /// </summary>
  public TEntity Record => record;

  public long Id => record is IEntity entity ? entity.Id : 0;

  public RowDto Row => row;

  public Dictionary<string, object?> Values => row.Cells;

  public object? this[string columnPath]
  {
    get => PropertyAccessorCache.GetPathValue(row.Cells, columnPath);
    set
    {
      if (string.IsNullOrWhiteSpace(columnPath))
      {
        return;
      }

      if (!columnPath.Contains('.'))
      {
        row.Cells[PropertyAccessorCache.ToCamel(columnPath)] = value;
        return;
      }

      var segments = PropertyAccessorCache.GetCamelPathSegments(columnPath);
      var current = row.Cells;
      for (var i = 0; i < segments.Length - 1; i++)
      {
        var key = segments[i];
        if (!current.TryGetValue(key, out var existing) || existing is not Dictionary<string, object?> nested)
        {
          nested = new Dictionary<string, object?>(4, StringComparer.Ordinal);
          current[key] = nested;
        }

        current = nested;
      }

      current[segments[^1]] = value;
    }
  }
}
