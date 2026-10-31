using Platform.Data.Internals;

namespace Platform.Data.Querying;

public sealed class QueryMapBuilder<TEntity> where TEntity : class
{
  private readonly List<Declaration> _declarations = [];
  private readonly List<SortRule> _defaultSort = [];
  private LambdaExpression? _key;
  private QueryLimits _limits = QueryLimits.Default;
  private NullOrdering _nullOrdering = NullOrdering.Native;

  internal QueryMapBuilder()
  {
  }

  public QueryMapBuilder<TEntity> Field(
    string name,
    LambdaExpression expression,
    Action<QueryFieldBuilder>? configure = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(expression);
    Declare(name, expression, configure);
    return this;
  }

  public QueryMapBuilder<TEntity> Field<TValue>(
    string name,
    Expression<Func<TEntity, TValue>> expression,
    Action<QueryFieldBuilder>? configure = null) =>
    Field(name, (LambdaExpression)expression, configure);

  public QueryMapBuilder<TEntity> Field<TValue>(
    Expression<Func<TEntity, TValue>> member,
    Action<QueryFieldBuilder>? configure = null) =>
    Field(MemberPath.Of(member), member, configure);

  public LambdaExpression? FindExpression(string name) =>
    _declarations.Find(d => Same(d.Name, name))?.Expression;

  public QueryMapBuilder<TEntity> Configure(string name, Action<QueryFieldBuilder> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    var declaration = _declarations.Find(d => Same(d.Name, name))
      ?? throw new InvalidOperationException($"The map of '{typeof(TEntity).Name}' declares no field '{name}' to configure.");
    configure(declaration.Options);
    return this;
  }

  public QueryMapBuilder<TEntity> Remove(string name)
  {
    _declarations.RemoveAll(d => Same(d.Name, name));
    return this;
  }

  public QueryMapBuilder<TEntity> Key<TKey>(Expression<Func<TEntity, TKey>> key)
  {
    ArgumentNullException.ThrowIfNull(key);
    _key = key;
    return this;
  }

  public QueryMapBuilder<TEntity> DefaultSort(params SortRule[] rules) => DefaultSort((IEnumerable<SortRule>)rules);

  public QueryMapBuilder<TEntity> DefaultSort(IEnumerable<SortRule> rules)
  {
    ArgumentNullException.ThrowIfNull(rules);
    _defaultSort.Clear();
    _defaultSort.AddRange(rules);
    return this;
  }

  public QueryMapBuilder<TEntity> Limits(QueryLimits limits)
  {
    ArgumentNullException.ThrowIfNull(limits);
    _limits = limits;
    return this;
  }

  public QueryMapBuilder<TEntity> NullsFirst() => NullsOrdered(NullOrdering.NullsFirst);
  public QueryMapBuilder<TEntity> NullsLast() => NullsOrdered(NullOrdering.NullsLast);
  public QueryMapBuilder<TEntity> NullsNative() => NullsOrdered(NullOrdering.Native);

  private QueryMapBuilder<TEntity> NullsOrdered(NullOrdering nullOrdering)
  {
    _nullOrdering = nullOrdering;
    return this;
  }

  public QueryMap<TEntity> Build()
  {
    var fields = _declarations.Select(Compile).ToList();
    var key = ResolveKey(fields);
    var defaultSort = _defaultSort.Select(rule => DefaultTerm(fields, rule)).ToList();

    return new QueryMap<TEntity>(fields, key, defaultSort, _limits, _nullOrdering);
  }

  internal QueryMapBuilder<TEntity> AddSelector<TRow>(Expression<Func<TEntity, TRow>> selector) =>
    DeclareAll(InferredFields.FromSelector(selector));

  public QueryMapBuilder<TEntity> AddEntityProperties(bool searchable = true)
  {
    foreach (var (name, expression) in InferredFields.FromEntity<TEntity>())
    {
      if (FindExpression(name) is null)
      {
        Declare(name, expression, searchable ? null : f => f.Searchable(false));
      }
    }

    return this;
  }

  private QueryMapBuilder<TEntity> DeclareAll(IReadOnlyList<(string Name, LambdaExpression Expression)> fields)
  {
    foreach (var (name, expression) in fields)
    {
      Declare(name, expression, null);
    }

    return this;
  }

  private void Declare(string name, LambdaExpression expression, Action<QueryFieldBuilder>? configure)
  {
    Remove(name);
    var declaration = new Declaration(name, expression, new QueryFieldBuilder());
    configure?.Invoke(declaration.Options);
    _declarations.Add(declaration);
  }

  private static QueryField Compile(Declaration declaration)
  {
    var (name, expression, options) = declaration;
    var type = expression.ReturnType;
    var collection = FieldKinds.CollectionOf(type);
    var kind = collection?.Kind ?? FieldKinds.Of(type)
      ?? throw new InvalidOperationException($"Field '{name}' of '{typeof(TEntity).Name}' is a '{type.Name}': no request value compares with it.");
    var isCollection = collection is not null;
    SensitiveGuard.Ensure(expression, name);

    var capabilities = isCollection
      ? (options.Resolve(kind) & FieldCapabilities.Filter)
      : options.Resolve(kind);
    if ((capabilities & FieldCapabilities.Search) != 0 && kind is not (FieldKind.Text or FieldKind.Number or FieldKind.Guid))
    {
      throw new InvalidOperationException($"Field '{name}' of '{typeof(TEntity).Name}' is {kind}: a search cannot run on it.");
    }

    var admitted = isCollection ? OperatorSets.ForCollection() : OperatorSets.For(kind);
    var foreign = options.AllowedOperators?.Where(o => !admitted.Contains(o)).ToList();
    if (foreign is { Count: > 0 })
    {
      throw new InvalidOperationException($"Field '{name}' of '{typeof(TEntity).Name}' is {kind} and does not admit '{foreign[0]}'.");
    }

    return new QueryField(
      name,
      expression,
      kind,
      capabilities,
      options.AllowedOperators ?? admitted,
      isCollection || StoreNullability.Of(expression),
      collection?.ElementType);
  }

  private static SortTerm DefaultTerm(List<QueryField> fields, SortRule rule)
  {
    var field = fields.Find(f => Same(f.Name, rule.Field))
      ?? throw new InvalidOperationException($"The default order of '{typeof(TEntity).Name}' names '{rule.Field}', which the map does not declare.");
    if (!SortDirection.Asc.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase) &&
        !SortDirection.Desc.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException(
        $"The default order of '{typeof(TEntity).Name}' sorts '{rule.Field}' by '{rule.Direction}'; a direction is 'asc' or 'desc'.");
    }

    return new SortTerm(field, SortDirection.Desc.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase));
  }

  private QueryField ResolveKey(List<QueryField> fields)
  {
    if (_key is null)
    {
      return fields.Find(f => Same(f.Name, "Id"))
        ?? throw new InvalidOperationException(
          $"The map of '{typeof(TEntity).Name}' has no key: paging needs a total order. Call Key(e => e.Id).");
    }

    var path = MemberPath.Of(_key);
    return fields.Find(f => MemberPath.TryOf(f.Expression) is { } declared && Same(declared, path))
      ?? Compile(new Declaration(path, _key, new QueryFieldBuilder().Capabilities(FieldCapabilities.None)));
  }

  private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

  private sealed record Declaration(string Name, LambdaExpression Expression, QueryFieldBuilder Options);
}
