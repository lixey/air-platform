using System.Collections.Frozen;

namespace Platform.Data.Querying;

/// <summary>
/// Compiled, immutable query schema for an entity: fields, tie-breaker key, default sort, and limits.
/// </summary>
public sealed class QueryMap<TEntity> where TEntity : class
{
  private readonly FrozenDictionary<string, QueryField> _fields;

  internal QueryMap(
    IEnumerable<QueryField> fields,
    QueryField key,
    IReadOnlyList<SortTerm> defaultSort,
    QueryLimits limits,
    NullOrdering nullOrdering)
  {
    _fields = fields.ToFrozenDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
    Key = key;
    DefaultSort = defaultSort;
    Limits = limits;
    NullOrdering = nullOrdering;
  }

  public IReadOnlyCollection<QueryField> Fields => _fields.Values;

  public QueryField Key { get; }
  public QueryLimits Limits { get; }
  public NullOrdering NullOrdering { get; }
  internal IReadOnlyList<SortTerm> DefaultSort { get; }

  public QueryField? Find(string name) => _fields.GetValueOrDefault(name);

  public QueryPlan<TEntity> Plan(ListRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    return new QueryPlanner<TEntity>(this).Plan(request);
  }

  public QueryMap<TEntity> Narrow(Func<QueryField, FieldCapabilities> capabilities)
  {
    ArgumentNullException.ThrowIfNull(capabilities);
    var narrowed = _fields.Values
      .Select(f => f.With(f.Capabilities & capabilities(f)))
      .Where(f => f.Capabilities != FieldCapabilities.None);
    return new QueryMap<TEntity>(narrowed, Key, DefaultSort, Limits, NullOrdering);
  }

  public QueryMap<TEntity> Narrow(Func<QueryField, bool> predicate)
  {
    ArgumentNullException.ThrowIfNull(predicate);
    return Narrow(f => predicate(f) ? f.Capabilities : FieldCapabilities.None);
  }

  public QueryMap<TEntity> Only(IEnumerable<string> names)
  {
    var kept = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    return Narrow(f => kept.Contains(f.Name) ? FieldCapabilities.All : FieldCapabilities.None);
  }

  internal QueryField Require(string? name, FieldCapabilities capability)
  {
    if (string.IsNullOrWhiteSpace(name) || !_fields.TryGetValue(name, out var field))
    {
      throw new QueryRequestException(QueryError.UnknownField, $"'{name}' is not a field of this list.", name);
    }

    if (!field.Can(capability))
    {
      var (error, verb) = capability switch
      {
        FieldCapabilities.Sort => (QueryError.NotSortable, "sorted by"),
        FieldCapabilities.Search => (QueryError.NotSearchable, "searched"),
        _ => (QueryError.NotFilterable, "filtered by")
      };
      throw new QueryRequestException(error, $"'{field.Name}' cannot be {verb}.", field.Name);
    }

    return field;
  }
}
