using Platform.App.Internals;

namespace Platform.App.Grid;

public readonly struct GridComputedRow<TEntity>(TEntity record, Dictionary<string, object?> values)
  where TEntity : class
{
  /// <summary>
  /// The projected entity instance for this row (includes all scalar properties of <typeparamref name="TEntity"/>
  /// and navigation properties referenced by declared grid columns).
  /// </summary>
  public TEntity Record => record;

  public long Id => record is IEntity entity ? entity.Id : 0;

  public Dictionary<string, object?> Values => values;

  public object? this[string columnPath]
  {
    get => PropertyAccessorCache.GetPathValue(values, columnPath);
    set
    {
      if (string.IsNullOrWhiteSpace(columnPath))
      {
        return;
      }

      if (!columnPath.Contains('.'))
      {
        values[PropertyAccessorCache.ToCamel(columnPath)] = value;
        return;
      }

      var segments = PropertyAccessorCache.GetCamelPathSegments(columnPath);
      var current = values;
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
