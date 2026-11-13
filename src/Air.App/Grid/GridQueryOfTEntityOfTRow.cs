using Platform.Data.Internals;

namespace Platform.App.Grid;

public sealed class GridQuery<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public List<Expression<Func<TEntity, bool>>> Filters { get; } = [];
  internal LambdaExpression? LabelSelector { get; private set; }
  internal Func<TEntity, string?>? CompiledLabelSelector { get; private set; }
  internal IReadOnlyList<string>? LabelNavigationPaths { get; private set; }

  public GridQuery<TEntity, TRow> RecordLabel<TProp>(Expression<Func<TEntity, TProp>> expr)
  {
    LabelSelector = expr;
    var compiled = expr.Compile();
    CompiledLabelSelector = e => compiled(e)?.ToString();
    LabelNavigationPaths = Form.Elements.ExtractNavigationPaths(expr);
    return this;
  }

  public GridQuery<TEntity, TRow> Where(Expression<Func<TEntity, bool>> predicate)
  {
    Filters.Add(predicate);
    return this;
  }

  public Expression<Func<TEntity, TRow>>? Selector { get; private set; }

  public GridQuery<TEntity, TRow> Select(Expression<Func<TEntity, TRow>> selector)
  {
    if (RowComputeAction != null)
    {
      throw new InvalidOperationException(
        "Query.Select cannot be combined with Query.Compute(GridComputedRow<TEntity>); use Query.Compute(IReadOnlyList<TRow>) or remove Query.Select.");
    }

    Selector = selector;
    return this;
  }

  public List<SortRule> DefaultSort { get; } = [];
  public int? CustomPageSize { get; private set; }
  public List<string>? SearchFields { get; private set; }

  public GridQuery<TEntity, TRow> PageSize(int pageSize)
  {
    if (pageSize > 0)
    {
      CustomPageSize = pageSize;
    }

    return this;
  }

  public List<FilterRule> InitialFilters { get; } = [];
  public List<string>? ReferenceLabelPaths { get; private set; }
  internal string? PickByField { get; private set; }
  internal Func<string, string>? PickKeySelector { get; private set; }
  public Func<IReadOnlyList<TRow>, CancellationToken, Task>? ComputeAction { get; private set; }
  public Func<IReadOnlyList<GridComputedRow<TEntity>>, CancellationToken, Task>? RowComputeAction { get; private set; }

  private static string? GetMemberPath(Expression expr)
  {
    if (expr is UnaryExpression ue)
    {
      expr = ue.Operand;
    }

    var parts = new List<string>();
    while (expr is MemberExpression me)
    {
      parts.Insert(0, char.ToLowerInvariant(me.Member.Name[0]) + me.Member.Name[1..]);
      expr = me.Expression!;
    }

    return parts.Count > 0 ? string.Join('.', parts) : null;
  }

  public GridQuery<TEntity, TRow> SortBy<TProp>(Expression<Func<TRow, TProp>> expr)
  {
    DefaultSort.Clear();
    var path = GetMemberPath(expr.Body);
    if (!string.IsNullOrEmpty(path))
    {
      DefaultSort.Add(SortRule.Asc(path));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> SortBy(string path)
  {
    DefaultSort.Clear();
    var camel = Columns.ToCamelPath(path);
    if (!string.IsNullOrEmpty(camel))
    {
      DefaultSort.Add(SortRule.Asc(camel));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> SortByDescending<TProp>(Expression<Func<TRow, TProp>> expr)
  {
    DefaultSort.Clear();
    var path = GetMemberPath(expr.Body);
    if (!string.IsNullOrEmpty(path))
    {
      DefaultSort.Add(SortRule.Desc(path));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> SortByDescending(string path)
  {
    DefaultSort.Clear();
    var camel = Columns.ToCamelPath(path);
    if (!string.IsNullOrEmpty(camel))
    {
      DefaultSort.Add(SortRule.Desc(camel));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> ThenBy<TProp>(Expression<Func<TRow, TProp>> expr)
  {
    var path = GetMemberPath(expr.Body);
    if (!string.IsNullOrEmpty(path))
    {
      DefaultSort.Add(SortRule.Asc(path));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> ThenBy(string path)
  {
    var camel = Columns.ToCamelPath(path);
    if (!string.IsNullOrEmpty(camel))
    {
      DefaultSort.Add(SortRule.Asc(camel));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> ThenByDescending<TProp>(Expression<Func<TRow, TProp>> expr)
  {
    var path = GetMemberPath(expr.Body);
    if (!string.IsNullOrEmpty(path))
    {
      DefaultSort.Add(SortRule.Desc(path));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> ThenByDescending(string path)
  {
    var camel = Columns.ToCamelPath(path);
    if (!string.IsNullOrEmpty(camel))
    {
      DefaultSort.Add(SortRule.Desc(camel));
    }

    return this;
  }

  public GridQuery<TEntity, TRow> SearchBy(params Expression<Func<TRow, object?>>[] exprs)
  {
    SearchFields = exprs
      .Select(e => GetMemberPath(e.Body) ?? "")
      .Where(s => !string.IsNullOrEmpty(s))
      .ToList();
    return this;
  }

  public GridQueryInitialFilterBuilder<TEntity, TRow, TProp> InitialFilter<TProp>(Expression<Func<TRow, TProp>> expr) =>
    new(this, GetMemberPath(expr.Body) ?? "");

  public GridQuery<TEntity, TRow> ReferenceLabel(params Expression<Func<TRow, object?>>[] label)
  {
    ReferenceLabelPaths = label
      .Select(expr => Columns.ToCamelPath(MemberPath.Of(expr)))
      .Where(s => !string.IsNullOrEmpty(s))
      .ToList();
    return this;
  }

  public GridQuery<TEntity, TRow> Compute(Func<IReadOnlyList<TRow>, CancellationToken, Task> compute)
  {
    ComputeAction = compute;
    return this;
  }

  public GridQuery<TEntity, TRow> Compute(Func<IReadOnlyList<GridComputedRow<TEntity>>, CancellationToken, Task> compute)
  {
    if (Selector != null)
    {
      throw new InvalidOperationException(
        "Query.Compute(GridComputedRow<TEntity>) cannot be combined with Query.Select; use Query.Compute(IReadOnlyList<TRow>) or remove Query.Select.");
    }

    RowComputeAction = compute;
    return this;
  }

  public GridQuery<TEntity, TRow> PickBy<TProp>(Expression<Func<TRow, TProp>> expr, Func<string, string>? keySelector = null)
  {
    var path = GetMemberPath(expr.Body);
    if (!string.IsNullOrEmpty(path))
    {
      PickByField = path;
      PickKeySelector = keySelector;
      SearchFields ??= [path];
    }

    return this;
  }

  public List<(string Name, Func<TRow, bool> Predicate)> RowFlags { get; } = [];

  public GridQuery<TEntity, TRow> FlagRows(string flagName, Func<TRow, bool> predicate)
  {
    if (!string.IsNullOrWhiteSpace(flagName) && predicate != null)
    {
      RowFlags.Add((flagName, predicate));
    }

    return this;
  }

  public List<GridNamedFilterItem<TEntity>> NamedFilters { get; } = [];
  public List<GridNamedFilterGroup<TEntity>> NamedFilterGroups { get; } = [];

  public GridNamedFilterBuilder<TEntity> NamedFilter(
    string key,
    string label,
    Expression<Func<TEntity, bool>> predicate)
  {
    var filter = new GridNamedFilterItem<TEntity>(key, label, predicate);
    NamedFilters.Add(filter);
    return new GridNamedFilterBuilder<TEntity>(filter);
  }

  public GridNamedFilterGroupBuilder<TEntity> NamedFilter(string key, string? label = null)
  {
    var group = new GridNamedFilterGroup<TEntity>(key, label);
    NamedFilterGroups.Add(group);
    return new GridNamedFilterGroupBuilder<TEntity>(group);
  }
}
