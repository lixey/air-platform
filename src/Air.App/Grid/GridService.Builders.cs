using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Grid;

public abstract partial class GridService<TEntity, TRow>
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public sealed class UserRestrictions
  {
    internal List<Expression<Func<TEntity, bool>>> Filters { get; } = [];

    public void Restrict(Expression<Func<TEntity, bool>> predicate)
    {
      Filters.Add(predicate);
    }
  }

  public sealed class Parent
  {
    internal Func<IQueryable<TEntity>, long, IQueryable<TEntity>>? FilterFunc { get; private set; }
    public Type? BoundParentServiceType { get; private set; }
    public Type? BoundParentEntityType => ParentScopeValidator.GetFormEntityType(BoundParentServiceType);
    public string? BoundParentService { get; private set; }
    public PropertyInfo? BoundProperty { get; private set; }
    public bool IsOptional { get; private set; }
    public bool HasRead { get; private set; }

    public bool MatchesService(string? parentService) =>
      ParentScopeValidator.MatchesService(parentService, BoundParentService, BoundParentServiceType);

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      BoundProperty = ParentScopeValidator.ExtractBoundProperty(member);

      var param = member.Parameters[0];
      FilterFunc = (q, parentId) =>
      {
        var body = Expression.Equal(member.Body, Expression.Constant(parentId));
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
        return q.Where(lambda);
      };
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long?>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      IsOptional = optional;
      BoundProperty = ParentScopeValidator.ExtractBoundProperty(member);

      var param = member.Parameters[0];
      FilterFunc = (q, parentId) =>
      {
        var body = Expression.Equal(member.Body, Expression.Constant((long?)parentId, typeof(long?)));
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
        return q.Where(lambda);
      };
    }

    public void Read<TParentService>()
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      HasRead = true;
    }

    internal void AllowDynamicParent()
    {
      HasRead = true;
    }
  }

  public sealed class Query
  {
    internal List<Expression<Func<TEntity, bool>>> Filters { get; } = [];
    internal LambdaExpression? LabelSelector { get; private set; }
    internal Func<TEntity, string?>? CompiledLabelSelector { get; private set; }
    internal IReadOnlyList<string>? LabelNavigationPaths { get; private set; }

    public Query RecordLabel<TProp>(Expression<Func<TEntity, TProp>> expr)
    {
      LabelSelector = expr;
      var compiled = expr.Compile();
      CompiledLabelSelector = e => compiled(e)?.ToString();
      LabelNavigationPaths = Form.Elements.ExtractNavigationPaths(expr);
      return this;
    }

    public Query Where(Expression<Func<TEntity, bool>> predicate)
    {
      Filters.Add(predicate);
      return this;
    }

    public Expression<Func<TEntity, TRow>>? Selector { get; private set; }

    public Query Select(Expression<Func<TEntity, TRow>> selector)
    {
      if (RowComputeAction != null)
      {
        throw new InvalidOperationException(
          "Query.Select cannot be combined with Query.Compute(GridComputedRow<TEntity>); use Query.Compute(IReadOnlyList<TRow>) or remove Query.Select.");
      }

      Selector = selector;
      return this;
    }

    internal List<SortRule> DefaultSort { get; } = [];
    internal int? CustomPageSize { get; private set; }
    internal List<string>? SearchFields { get; private set; }

    public Query PageSize(int pageSize)
    {
      if (pageSize > 0)
      {
        CustomPageSize = pageSize;
      }

      return this;
    }

    internal List<FilterRule> InitialFilters { get; } = [];
    internal List<string>? ReferenceLabelPaths { get; private set; }
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

    public Query SortBy<TProp>(Expression<Func<TRow, TProp>> expr)
    {
      DefaultSort.Clear();
      var path = GetMemberPath(expr.Body);
      if (!string.IsNullOrEmpty(path))
      {
        DefaultSort.Add(SortRule.Asc(path));
      }

      return this;
    }

    public Query SortBy(string path)
    {
      DefaultSort.Clear();
      var camel = Columns.ToCamelPath(path);
      if (!string.IsNullOrEmpty(camel))
      {
        DefaultSort.Add(SortRule.Asc(camel));
      }

      return this;
    }

    public Query SortByDescending<TProp>(Expression<Func<TRow, TProp>> expr)
    {
      DefaultSort.Clear();
      var path = GetMemberPath(expr.Body);
      if (!string.IsNullOrEmpty(path))
      {
        DefaultSort.Add(SortRule.Desc(path));
      }

      return this;
    }

    public Query SortByDescending(string path)
    {
      DefaultSort.Clear();
      var camel = Columns.ToCamelPath(path);
      if (!string.IsNullOrEmpty(camel))
      {
        DefaultSort.Add(SortRule.Desc(camel));
      }

      return this;
    }

    public Query ThenBy<TProp>(Expression<Func<TRow, TProp>> expr)
    {
      var path = GetMemberPath(expr.Body);
      if (!string.IsNullOrEmpty(path))
      {
        DefaultSort.Add(SortRule.Asc(path));
      }

      return this;
    }

    public Query ThenBy(string path)
    {
      var camel = Columns.ToCamelPath(path);
      if (!string.IsNullOrEmpty(camel))
      {
        DefaultSort.Add(SortRule.Asc(camel));
      }

      return this;
    }

    public Query ThenByDescending<TProp>(Expression<Func<TRow, TProp>> expr)
    {
      var path = GetMemberPath(expr.Body);
      if (!string.IsNullOrEmpty(path))
      {
        DefaultSort.Add(SortRule.Desc(path));
      }

      return this;
    }

    public Query ThenByDescending(string path)
    {
      var camel = Columns.ToCamelPath(path);
      if (!string.IsNullOrEmpty(camel))
      {
        DefaultSort.Add(SortRule.Desc(camel));
      }

      return this;
    }

    public Query SearchBy(params Expression<Func<TRow, object?>>[] exprs)
    {
      SearchFields = exprs
        .Select(e => GetMemberPath(e.Body) ?? "")
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
      return this;
    }

    public InitialFilterBuilder<TProp> InitialFilter<TProp>(Expression<Func<TRow, TProp>> expr) =>
      new(this, GetMemberPath(expr.Body) ?? "");

    public Query ReferenceLabel(params Expression<Func<TRow, object?>>[] label)
    {
      ReferenceLabelPaths = label
        .Select(expr => Columns.ToCamelPath(MemberPath.Of(expr)))
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
      return this;
    }

    public Query Compute(Func<IReadOnlyList<TRow>, CancellationToken, Task> compute)
    {
      ComputeAction = compute;
      return this;
    }

    public Query Compute(Func<IReadOnlyList<GridComputedRow<TEntity>>, CancellationToken, Task> compute)
    {
      if (Selector != null)
      {
        throw new InvalidOperationException(
          "Query.Compute(GridComputedRow<TEntity>) cannot be combined with Query.Select; use Query.Compute(IReadOnlyList<TRow>) or remove Query.Select.");
      }

      RowComputeAction = compute;
      return this;
    }

    public Query PickBy<TProp>(Expression<Func<TRow, TProp>> expr, Func<string, string>? keySelector = null)
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

    internal List<(string Name, Func<TRow, bool> Predicate)> RowFlags { get; } = [];

    public Query FlagRows(string flagName, Func<TRow, bool> predicate)
    {
      if (!string.IsNullOrWhiteSpace(flagName) && predicate != null)
      {
        RowFlags.Add((flagName, predicate));
      }

      return this;
    }

    internal List<GridNamedFilterItem<TEntity>> NamedFilters { get; } = [];
    internal List<GridNamedFilterGroup<TEntity>> NamedFilterGroups { get; } = [];

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

    public sealed class InitialFilterBuilder<TProp>(Query query, string field)
    {
      public Query In(params TProp[] values)
      {
        if (!string.IsNullOrEmpty(field) && values.Length > 0)
        {
          query.InitialFilters.Add(FilterNode.Rule(field, FilterOperator.In, values.Cast<object?>().ToList()));
        }

        return query;
      }
    }
  }

  public sealed class Actions : GridActions<TEntity>;

  public sealed class Nav(Localizer? localizer = null)
  {
    private readonly Localizer _l = localizer ?? Localizer.Instance;
    internal List<GridNavPickerModel> Pickers { get; } = [];
    internal List<(Type SourceType, MethodInfo Method, GridNavPickerModel Model)> PickerEntries { get; } = [];
    internal List<NavElementModel> CustomStart { get; } = [];
    internal List<(Type FormType, GridNavAddModel Model)> Creates { get; } = [];

    public void AddStart(NavElementModel model) => CustomStart.Add(model);

    public void AddCreate<TFormService>(Action<GridButtonBuilder>? look = null) where TFormService : class
    {
      var b = new GridButtonBuilder();
      look?.Invoke(b);
      var targetService = ServicePathResolver.Resolve(typeof(TFormService));
      var rawLabel = b.LabelText ?? (b.TitleText != null ? null : "Add");
      Creates.Add((typeof(TFormService), new GridNavAddModel
      {
        Key = $"add.{targetService.Replace('/', '.')}",
        Service = targetService,
        Label = !string.IsNullOrEmpty(rawLabel) ? _l[rawLabel] : null,
        Title = b.TitleText != null ? _l[b.TitleText] : null,
        Icon = b.IconHandle ?? UiIcon.Plus,
        Color = b.ColorHandle ?? UiColor.Orange,
        Variant = b.VariantHandle ?? ButtonVariant.Soft
      }));
    }

    public GridNavPickerBuilder AddPicker<TSource>(
      Delegate method,
      string? label = null)
    {
      var readsParent = ParentScopeValidator.DeclaresParentScope(typeof(TSource));
      var picker = new GridNavPickerModel
      {
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = label != null ? _l[label] : null,
        ReferenceService = ServicePathResolver.Resolve(typeof(TSource)),
        MinLength = 2,
        ReadsParent = readsParent
      };
      Pickers.Add(picker);
      PickerEntries.Add((typeof(TSource), method.Method, picker));
      return new GridNavPickerBuilder(picker);
    }
  }

  public sealed class Hooks(string? moduleHandle = null)
  {
    internal bool IsMultiDeleteAllowed { get; private set; }
    internal List<Type> DependentEntityTypes { get; } = [];
    internal List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> BeforeDeleteActions { get; } = [];
    internal List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> AfterDeleteActions { get; } = [];

    public void AllowMultiDelete(bool allow = true) => IsMultiDeleteAllowed = allow;

    public void PreventDeleteWhenUsedBy<TDependent>(
      Expression<Func<TDependent, long>> foreignKey,
      string? message = null)
      where TDependent : class
    {
      ArgumentNullException.ThrowIfNull(foreignKey);
      DependentEntityTypes.Add(typeof(TDependent));
      BeforeDeleteActions.Add(async (c, ct) =>
      {
        if (c.Db is null || c.Records.IsEmpty)
        {
          return;
        }

        var ids = c.Records.Select(r => r.Id).Distinct().ToArray();
        var query = c.Db.Set<TDependent>()
          .AsNoTracking()
          .Select(foreignKey)
          .Where(fk => ids.Contains(fk))
          .Distinct();
        var usedIds = query is IAsyncEnumerable<long>
          ? await query.ToListAsync(ct)
          : query.ToList();
        RefuseUsedRecords(c, usedIds, message);
      });
    }

    public void PreventDeleteWhenUsedBy<TDependent>(
      Expression<Func<TDependent, long?>> foreignKey,
      string? message = null)
      where TDependent : class
    {
      ArgumentNullException.ThrowIfNull(foreignKey);
      DependentEntityTypes.Add(typeof(TDependent));
      BeforeDeleteActions.Add(async (c, ct) =>
      {
        if (c.Db is null || c.Records.IsEmpty)
        {
          return;
        }

        var ids = c.Records.Select(r => r.Id).Distinct().ToArray();
        var query = c.Db.Set<TDependent>()
          .AsNoTracking()
          .Select(foreignKey)
          .Where(fk => fk.HasValue && ids.Contains(fk.Value))
          .Select(fk => fk!.Value)
          .Distinct();
        var usedIds = query is IAsyncEnumerable<long>
          ? await query.ToListAsync(ct)
          : query.ToList();
        RefuseUsedRecords(c, usedIds, message);
      });
    }

    private void RefuseUsedRecords(GridDeleteContext<TEntity> context, List<long> usedIds, string? message)
    {
      if (usedIds.Count == 0)
      {
        return;
      }

      var loc = !string.IsNullOrEmpty(moduleHandle)
        ? Localizer.Instance.For(moduleHandle)
        : Localizer.Instance;
      var text = !string.IsNullOrEmpty(message) ? loc[message] : loc["DeleteRefusedInUse"];
      foreach (var usedId in usedIds)
      {
        if (!context.Errors.Any(e => e.RecordId == usedId && string.Equals(e.Title, text, StringComparison.Ordinal)))
        {
          context.Refuse(text, usedId);
        }
      }
    }

    public void BeforeDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);
    public void BeforeDelete(Action<GridDeleteContext<TEntity>> handler) =>
      BeforeDeleteActions.Add((c, _) =>
      {
        handler(c);
        return Task.CompletedTask;
      });

    public void AfterDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => AfterDeleteActions.Add(handler);
    public void AfterDelete(Action<GridDeleteContext<TEntity>> handler) =>
      AfterDeleteActions.Add((c, _) =>
      {
        handler(c);
        return Task.CompletedTask;
      });
  }

  public sealed class Columns : Columns<TEntity, TRow>;
}
