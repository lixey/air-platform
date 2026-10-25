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
    public string? BoundParentService { get; private set; }
    public PropertyInfo? BoundProperty { get; private set; }
    public bool HasRead { get; private set; }

    public bool MatchesService(string? parentService)
    {
      if (string.IsNullOrEmpty(parentService) || string.IsNullOrEmpty(BoundParentService))
      {
        return true;
      }

      if (string.Equals(parentService, BoundParentService, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      if (BoundParentServiceType != null &&
          ModuleCatalog.Instance.FindServiceType(parentService) is { } actualType)
      {
        if (BoundParentServiceType.IsAssignableFrom(actualType) ||
            actualType.IsAssignableFrom(BoundParentServiceType))
        {
          return true;
        }

        if (GetFormEntityType(BoundParentServiceType) is { } expectedEntity &&
            expectedEntity == GetFormEntityType(actualType))
        {
          return true;
        }
      }

      return false;
    }

    private static Type? GetFormEntityType(Type? type)
    {
      for (var t = type; t != null && t != typeof(object); t = t.BaseType)
      {
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Form.FormService<,>))
        {
          return t.GetGenericArguments()[0];
        }
      }

      return null;
    }

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
      BoundParentServiceType = typeof(TParentService);
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      if (member.Body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        BoundProperty = pi;
      }
      else if (member.Body is UnaryExpression ue && ue.Operand is MemberExpression ueme && ueme.Member is PropertyInfo upi)
      {
        BoundProperty = upi;
      }

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
      if (member.Body is MemberExpression me && me.Member is PropertyInfo pi)
      {
        BoundProperty = pi;
      }
      else if (member.Body is UnaryExpression ue && ue.Operand is MemberExpression ueme && ueme.Member is PropertyInfo upi)
      {
        BoundProperty = upi;
      }

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
  }

  public sealed class Query
  {
    internal List<Expression<Func<TEntity, bool>>> Filters { get; } = [];
    internal LambdaExpression? LabelSelector { get; private set; }

    public Query RecordLabel<TProp>(Expression<Func<TEntity, TProp>> expr)
    {
      LabelSelector = expr;
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

    private static string? GetMemberName(Expression expr)
    {
      if (expr is UnaryExpression ue)
      {
        expr = ue.Operand;
      }

      return expr is MemberExpression me ? me.Member.Name : null;
    }

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

    public Query ThenBy<TProp>(Expression<Func<TRow, TProp>> expr)
    {
      var path = GetMemberPath(expr.Body);
      if (!string.IsNullOrEmpty(path))
      {
        DefaultSort.Add(SortRule.Asc(path));
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
        .Select(expr => GetMemberName(expr.Body)?.ToLowerInvariant() ?? "")
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
      return this;
    }

    public Query Compute(Func<IReadOnlyList<TRow>, CancellationToken, Task> compute)
    {
      ComputeAction = compute;
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

    internal List<NamedFilterItem> NamedFilters { get; } = [];
    internal List<NamedFilterGroup> NamedFilterGroups { get; } = [];

    public GridNamedFilterBuilder NamedFilter(
      string key,
      string label,
      Expression<Func<TEntity, bool>> predicate)
    {
      var filter = new NamedFilterItem(key, label, predicate);
      NamedFilters.Add(filter);
      return new GridNamedFilterBuilder(filter);
    }

    public GridNamedFilterGroupBuilder NamedFilter(string key, string? label = null)
    {
      var group = new NamedFilterGroup(key, label);
      NamedFilterGroups.Add(group);
      return new GridNamedFilterGroupBuilder(group);
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

  public sealed class GridNamedFilterBuilder(NamedFilterItem item)
  {
    public GridNamedFilterBuilder Remember(bool remember = true)
    {
      item.IsRemembered = remember;
      return this;
    }

    public GridNamedFilterBuilder Title(string title)
    {
      item.Title = title;
      return this;
    }

    public GridNamedFilterBuilder Title(string legacyUi, string legacyDb)
    {
      item.Title = $"Легасі UI: {legacyUi} | БД: {legacyDb}";
      return this;
    }
  }

  public sealed class GridNamedFilterGroupBuilder(NamedFilterGroup group)
  {
    public GridNamedFilterGroupBuilder Item(string key, string label, Expression<Func<TEntity, bool>> predicate)
    {
      group.Items.Add(new NamedFilterItem($"{group.Key}.{key}", label, predicate));
      return this;
    }

    public GridNamedFilterGroupBuilder All(string label)
    {
      group.Items.Add(new NamedFilterItem($"{group.Key}.all", label, null));
      return this;
    }

    public GridNamedFilterGroupBuilder Default()
    {
      if (group.Items.Count > 0)
      {
        group.Items[^1].IsDefault = true;
      }

      return this;
    }

    public GridNamedFilterGroupBuilder Color(string color)
    {
      if (group.Items.Count > 0)
      {
        group.Items[^1].Color = color;
      }

      return this;
    }

    public GridNamedFilterGroupBuilder Title(string title)
    {
      if (group.Items.Count > 0)
      {
        group.Items[^1].Title = title;
      }

      return this;
    }

    public GridNamedFilterGroupBuilder Title(string legacyUi, string legacyDb) =>
      Title($"Легасі UI: {legacyUi} | БД: {legacyDb}");

    public GridNamedFilterGroupBuilder Remember(bool remember = true)
    {
      group.IsRemembered = remember;
      return this;
    }
  }

  public sealed class NamedFilterItem(string key, string label, Expression<Func<TEntity, bool>>? predicate)
  {
    public string Key { get; } = key;
    public string Label { get; } = label;
    public Expression<Func<TEntity, bool>>? Predicate { get; } = predicate;
    public bool IsRemembered { get; set; }
    public bool IsDefault { get; set; }
    public string? Color { get; set; }
    public string? Title { get; set; }
  }

  public sealed class NamedFilterGroup(string key, string? label)
  {
    public string Key { get; } = key;
    public string? Label { get; } = label;
    public bool IsRemembered { get; set; }
    public List<NamedFilterItem> Items { get; } = [];
  }

  public sealed class ButtonBuilder
  {
    public string? LabelText { get; private set; }
    public string? TitleText { get; private set; }
    public string? IconHandle { get; private set; }
    public string? ColorHandle { get; private set; }
    public string? VariantHandle { get; private set; }
    public bool IsInMenu { get; private set; }
    public bool IsSingle { get; private set; }

    public ButtonBuilder Label(string label)
    {
      LabelText = label;
      return this;
    }
    public ButtonBuilder Title(string title)
    {
      TitleText = title;
      return this;
    }
    public ButtonBuilder Title(string legacyUi, string legacyDb)
    {
      TitleText = $"Легасі UI: {legacyUi} | БД: {legacyDb}";
      return this;
    }
    public ButtonBuilder Icon(string icon)
    {
      IconHandle = icon;
      return this;
    }
    public ButtonBuilder Color(string color)
    {
      ColorHandle = color;
      return this;
    }
    public ButtonBuilder Variant(string variant)
    {
      VariantHandle = variant;
      return this;
    }
    public ButtonBuilder Soft()
    {
      VariantHandle = ButtonVariant.Soft;
      return this;
    }
    public ButtonBuilder InMenu()
    {
      IsInMenu = true;
      return this;
    }
    public ButtonBuilder Single(bool single = true)
    {
      IsSingle = single;
      return this;
    }
  }

  public sealed class NavPickerBuilder(GridNavPickerModel model)
  {
    public NavPickerBuilder MinLength(int len)
    {
      model.MinLength = len;
      return this;
    }
    public NavPickerBuilder ReadsParent()
    {
      model.ReadsParent = true;
      return this;
    }
  }

  public sealed class NavActionEntry
  {
    public required GridNavActionModel Model { get; init; }
    public bool IsInMenu { get; set; }
    public bool IsAllowedInReadOnly { get; set; }
    public Delegate Method { get; init; } = null!;
    public Screens.RecordAsk? Ask { get; set; }
    public Func<CancellationToken, Task<object?>>? DefaultProvider { get; set; }
    public List<Screens.RecordAvailability> Offers { get; } = [];
  }

  public class NavActionBuilder(NavActionEntry entry, Nav nav)
  {
    public Nav InMenu()
    {
      entry.IsInMenu = true;
      return nav;
    }

    public NavActionBuilder AllowReadOnly(bool allow = true)
    {
      entry.IsAllowedInReadOnly = allow;
      return this;
    }

    public NavActionBuilder Single(bool single = true)
    {
      entry.Model.IsSingle = single;
      return this;
    }
  }

  public sealed class NavActionBuilder<TRequest>(NavActionEntry entry, Nav nav) : NavActionBuilder(entry, nav)
    where TRequest : class, new()
  {
    public NavActionBuilder<TRequest> Ask(Action<Form.Actions.ActionInput<TRequest>> elements)
    {
      entry.Ask = Form.Actions.TransitionAsk.Of(elements);
      return this;
    }

    public NavActionBuilder<TRequest> Default(Func<CancellationToken, Task<TRequest>> provider)
    {
      entry.DefaultProvider = async ct => await provider(ct);
      return this;
    }

    public NavActionBuilder<TRequest> Default(Func<TRequest> provider)
    {
      entry.DefaultProvider = _ => Task.FromResult<object?>(provider());
      return this;
    }

    public NavActionBuilder<TRequest> Default(Func<TRequest, CancellationToken, Task> provider)
    {
      entry.DefaultProvider = async ct =>
      {
        var req = new TRequest();
        await provider(req, ct);
        return req;
      };
      return this;
    }

    public NavActionBuilder<TRequest> Default(Action<TRequest> provider)
    {
      entry.DefaultProvider = _ =>
      {
        var req = new TRequest();
        provider(req);
        return Task.FromResult<object?>(req);
      };
      return this;
    }

    public new NavActionBuilder<TRequest> AllowReadOnly(bool allow = true)
    {
      base.AllowReadOnly(allow);
      return this;
    }

    public new NavActionBuilder<TRequest> Single(bool single = true)
    {
      base.Single(single);
      return this;
    }
  }

  public sealed class Nav(string servicePath = "", Localizer? localizer = null)
  {
    private readonly Localizer _l = localizer ?? Localizer.Instance;
    internal List<NavActionEntry> Actions { get; } = [];
    internal List<GridNavPickerModel> Pickers { get; } = [];
    internal List<NavElementModel> CustomStart { get; } = [];
    internal List<(Type FormType, GridNavAddModel Model)> Creates { get; } = [];

    public void AddStart(NavElementModel model) => CustomStart.Add(model);

    public void AddCreate<TFormService>(Action<ButtonBuilder>? look = null) where TFormService : class
    {
      var b = new ButtonBuilder();
      look?.Invoke(b);
      var targetService = ServicePathResolver.Resolve(typeof(TFormService));
      var rawLabel = b.LabelText ?? "Add";
      Creates.Add((typeof(TFormService), new GridNavAddModel
      {
        Key = $"add.{targetService.Replace('/', '.')}",
        Service = targetService,
        Label = _l[rawLabel],
        Title = b.TitleText != null ? _l[b.TitleText] : null,
        Icon = b.IconHandle ?? UiIcon.Plus,
        Color = b.ColorHandle ?? UiColor.Orange,
        Variant = b.VariantHandle ?? ButtonVariant.Soft
      }));
    }

    public NavActionBuilder AddAction(
      Delegate method,
      Action<ButtonBuilder>? look = null,
      string? confirm = null,
      bool needsSelection = true)
    {
      var b = new ButtonBuilder();
      look?.Invoke(b);
      var rawLabel = b.LabelText ?? method.Method.Name;
      var action = new GridNavActionModel
      {
        Service = servicePath,
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = _l[rawLabel],
        Title = b.TitleText != null ? _l[b.TitleText] : null,
        Icon = b.IconHandle,
        Color = b.ColorHandle ?? UiColor.Blue,
        Variant = b.VariantHandle ?? ButtonVariant.Soft,
        NeedsSelection = needsSelection,
        IsSingle = b.IsSingle,
        Confirm = confirm != null ? new OverlayConfirmModel { Message = _l[confirm], Color = UiColor.Red } : null
      };
      var entry = new NavActionEntry
      {
        Model = action,
        IsInMenu = b.IsInMenu,
        Method = method
      };
      Actions.Add(entry);
      return new NavActionBuilder(entry, this);
    }

    public NavActionBuilder<TRequest> AddAction<TRequest>(
      Delegate method,
      Action<ButtonBuilder>? look = null,
      string? confirm = null,
      bool needsSelection = true)
      where TRequest : class, new()
    {
      var b = new ButtonBuilder();
      look?.Invoke(b);
      var rawLabel = b.LabelText ?? method.Method.Name;
      var action = new GridNavActionModel
      {
        Service = servicePath,
        Key = method.Method.Name.ToLowerInvariant(),
        Method = method.Method.Name,
        Label = _l[rawLabel],
        Title = b.TitleText != null ? _l[b.TitleText] : null,
        Icon = b.IconHandle,
        Color = b.ColorHandle ?? UiColor.Blue,
        Variant = b.VariantHandle ?? ButtonVariant.Soft,
        NeedsSelection = needsSelection,
        IsSingle = b.IsSingle,
        Confirm = confirm != null ? new OverlayConfirmModel { Message = _l[confirm], Color = b.ColorHandle ?? UiColor.Red } : null
      };
      var entry = new NavActionEntry
      {
        Model = action,
        IsInMenu = b.IsInMenu,
        Method = method
      };
      Actions.Add(entry);
      return new NavActionBuilder<TRequest>(entry, this);
    }

    public void AvailableWhen(
      Delegate action,
      Expression<Func<TEntity, bool>> condition,
      string reason) =>
      AvailableWhen(condition, reason, action);

    public void AvailableWhen(
      Expression<Func<TEntity, bool>> condition,
      string reason,
      params Delegate[] actions)
    {
      var methodNames = actions.Select(a => a.Method.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var entry in Actions)
      {
        if (methodNames.Count == 0 || methodNames.Contains(entry.Method.Method.Name))
        {
          entry.Offers.Add(new Screens.RecordAvailability { Condition = condition, Reason = reason });
          entry.Model.UnavailableReasons ??= [];
          entry.Model.UnavailableReasons.Add(_l[reason]);
        }
      }
    }

    public NavPickerBuilder AddPicker<TSource>(
      Delegate method,
      string? label = null)
    {
      var setParentMethod = typeof(TSource).GetMethod("SetParent", BindingFlags.Instance | BindingFlags.NonPublic);
      var readsParent = setParentMethod != null && setParentMethod.DeclaringType != null && !setParentMethod.DeclaringType.IsGenericType;
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
      return new NavPickerBuilder(picker);
    }
  }

  public sealed class Hooks
  {
    internal List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> BeforeDeleteActions { get; } = [];
    internal List<Func<GridDeleteContext<TEntity>, CancellationToken, Task>> AfterDeleteActions { get; } = [];

    public void BeforeDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDeleteActions.Add(handler);
    public void BeforeDelete(Action<GridDeleteContext<TEntity>> handler) =>
      BeforeDeleteActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });
    public void ValidateDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => BeforeDelete(handler);
    public void ValidateDelete(Action<GridDeleteContext<TEntity>> handler) => BeforeDelete(handler);

    public void AfterDelete(Func<GridDeleteContext<TEntity>, CancellationToken, Task> handler) => AfterDeleteActions.Add(handler);
    public void AfterDelete(Action<GridDeleteContext<TEntity>> handler) =>
      AfterDeleteActions.Add((c, ct) =>
      {
        handler(c);
        return Task.CompletedTask;
      });
  }

  public sealed class Columns : Columns<TRow>
  {
    public new GridColumn<TEntity, TRow, TProp> Add<TProp>(Expression<Func<TRow, TProp>> expression)
    {
      var col = base.Add(expression);
      return new GridColumn<TEntity, TRow, TProp>(col.Dto, this);
    }

    public new GridColumn<TEntity, TRow, TProp> Add<TProp>(string path)
    {
      var col = base.Add<TProp>(path);
      return new GridColumn<TEntity, TRow, TProp>(col.Dto, this);
    }
  }
}
