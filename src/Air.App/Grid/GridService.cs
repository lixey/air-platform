namespace Air.App.Grid;

public sealed record GridCreateDescriptor(Type FormServiceType, string ServicePath, string Label, string? Color = null);

public interface IGridService
{
  PlatformDbContext DbContext { get; }
  Task<ScreenDto> BuildScreenAsync(string servicePath, CancellationToken ct = default);
  Task<ScreenDto> BuildScreenAsync(string servicePath, GridRequest? request, CancellationToken ct = default) => BuildScreenAsync(servicePath, ct);
  Task<Page<object>> GetDataAsync(ListRequest request, CancellationToken ct = default);
  Task<GridCountDto> GetCountAsync(GridRequest request, CancellationToken ct = default);
  Task<IResult> ExportAsync(GridRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> DeleteAsync(GridRequest request, CancellationToken ct = default);
  Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) => DeleteAsync(request, cancellationToken);
  Task<GridActionDto> MoveStatusAsync(GridRequest request, string transitionName, JsonElement? body, CancellationToken ct = default);
  Task<GridCellDto> SaveCellAsync(GridCellRequest request, IServiceProvider services, CancellationToken ct = default);
  Task<object?> ReadRowAsync(long id, CancellationToken ct = default);
  IReadOnlyList<string>? GetReferenceLabelPaths() => null;
  string? GetRecordLabel(object? entity) => null;
  IReadOnlyList<GridCreateDescriptor> GetCreateForms() => [];
  void AttachPlatformApp(PlatformAppService app);
  Feature Feature { get; }
}

public abstract partial class GridService<TEntity, TRow>(PlatformDbContext db, PlatformAppService? app = null) : IGridService
  where TEntity : class, IEntity, new()
  where TRow : class
{
  public void AttachPlatformApp(PlatformAppService service) => PlatformApp ??= service;

  public PlatformDbContext DbContext => db;
  public PlatformAppService? PlatformApp { get; private set; } = app;
  protected Auth.IUserContext? User => PlatformApp?.User;
  protected Localizer L => Localizer.Instance.For(Feature.Module);

  public virtual Feature Feature => new("", GetType().Name.Replace("GridService", "").ToLowerInvariant(), GetType());

  protected virtual bool IsReadOnly => Feature.IsReadOnly || User != null && User.IsAuthenticated && !string.IsNullOrEmpty(Feature.Module) && !User.Can(Feature.Edit) && !User.Can(Feature.All) && !User.Can("*");
  protected virtual string FiltersWidth => PanelSize.Px(720);
  protected virtual string? FormServicePath => null;

  public sealed class Parent
  {
    internal Func<IQueryable<TEntity>, long, IQueryable<TEntity>>? FilterFunc { get; private set; }
    public string? BoundParentService { get; private set; }
    public PropertyInfo? BoundProperty { get; private set; }
    public bool HasRead { get; private set; }

    public void Bind<TParentService>(Expression<Func<TEntity, long>> member, bool optional = false)
    {
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
      BoundParentService = ServicePathResolver.Resolve(typeof(TParentService));
      HasRead = true;
    }
  }

  protected virtual void SetParent(Parent parent) { }
  protected internal long? ParentId { get; set; }

  protected TParent? ParentRecord<TParent>() where TParent : class, IEntity
  {
    if (ParentId == null || ParentId.Value <= 0)
    {
      return null;
    }

    var tracked = db.Set<TParent>().Local.FirstOrDefault(e => e.Id == ParentId.Value);
    if (tracked != null)
    {
      return tracked;
    }

    var efModel = db.Model.FindEntityType(typeof(TParent));
    IQueryable<TParent> query = db.Set<TParent>();
    if (efModel != null)
    {
      foreach (var nav in efModel.GetNavigations())
      {
        if (!nav.IsCollection)
        {
          query = query.Include(nav.Name);
        }
      }
    }

    return query.FirstOrDefault(e => e.Id == ParentId.Value);
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
    public GridNamedFilterBuilder Badge(bool badge = true)
    {
      item.HasBadge = badge;
      return this;
    }

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

    public GridNamedFilterGroupBuilder Badge(bool badge = true)
    {
      if (group.Items.Count > 0)
      {
        group.Items[^1].HasBadge = badge;
      }

      return this;
    }

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
    public bool HasBadge { get; set; }
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

  public virtual bool CanDelete()
  {
    if (IsReadOnly)
    {
      return false;
    }

    var deleteMethod = GetType().GetMethod(nameof(Delete), BindingFlags.Public | BindingFlags.Instance)
      ?? GetType().GetMethod(nameof(DeleteAsync), BindingFlags.Public | BindingFlags.Instance);

    if (deleteMethod != null)
    {
      if (deleteMethod.GetCustomAttribute<Security.RequireRightAttribute>(inherit: true) is { } reqRight)
      {
        if (Feature.FeaturesByServiceType.TryGetValue(reqRight.Service, out var targetFeat))
        {
          return User == null || !User.IsAuthenticated || User.Can(targetFeat.Action(reqRight.Action.ToString())) || User.Can(targetFeat.All);
        }
      }

      if (deleteMethod.GetCustomAttribute<Security.RequireActionAttribute>(inherit: true) is { } reqAction)
      {
        return User == null || !User.IsAuthenticated || User.Can(Feature.Action(reqAction.Action.ToString())) || User.Can(Feature.All);
      }
    }

    return Feature.HasDelete && (User == null || !User.IsAuthenticated || User.Can(Feature.Delete) || User.Can(Feature.All));
  }

  public virtual Task<GridDeleteDto> Delete(GridRequest request, CancellationToken cancellationToken = default) =>
    DeleteAsync(request, cancellationToken);

  public virtual IReadOnlyList<string>? GetReferenceLabelPaths()
  {
    var q = new Query();
    SetQuery(q);
    return q.ReferenceLabelPaths;
  }

  public virtual string? GetRecordLabel(object? entity)
  {
    if (entity is not TEntity typed)
    {
      return null;
    }

    var q = new Query();
    SetQuery(q);
    if (q.LabelSelector != null)
    {
      try
      {
        return q.LabelSelector.Compile().DynamicInvoke(typed)?.ToString();
      }
      catch
      {
        return null;
      }
    }

    return null;
  }

  protected virtual void SetQuery(Query query) { }

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
  }

  public sealed class NavActionBuilder(NavActionEntry entry, Nav nav)
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

  protected virtual void SetNav(Nav nav) { }

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

  protected virtual void SetHooks(Hooks hooks) { }

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

  protected virtual void SetColumns(Columns columns) { }
  protected virtual void SetColumns(Columns<TRow> columns) { }

  private Columns CreateColumns()
  {
    var cols = new Columns();
    SetColumns(cols);
    SetColumns((Columns<TRow>)cols);
    return cols;
  }

  protected virtual IQueryable<TEntity> BaseQuery(PlatformDbContext context) => context.Set<TEntity>();

  private Type? ResolveFormServiceType(Columns columns)
  {
    if (columns.FormServiceType != null)
    {
      return columns.FormServiceType;
    }

    if (FormServicePath != null)
    {
      return ModuleCatalog.Instance.FindServiceType(FormServicePath);
    }

    var candidateFormName = GetType().Name.Replace("GridService", "FormService");
    return GetType().Assembly.GetType(GetType().Namespace + "." + candidateFormName)
      ?? GetType().Assembly.GetTypes().FirstOrDefault(t => t.Name == candidateFormName);
  }

  private string? ResolveFormServicePath(Columns columns)
  {
    if (FormServicePath != null)
    {
      return FormServicePath;
    }

    var formType = ResolveFormServiceType(columns);
    return formType != null ? ServicePathResolver.Resolve(formType) : null;
  }

  public virtual IReadOnlyList<GridCreateDescriptor> GetCreateForms()
  {
    if (IsReadOnly)
    {
      return [];
    }

    var servicePath = ServicePathResolver.Resolve(GetType());
    var nav = new Nav(servicePath, L);
    SetNav(nav);
    if (nav.Creates.Count > 0)
    {
      return nav.Creates
        .Select(c => new GridCreateDescriptor(
          c.FormType,
          c.Model.Service ?? ServicePathResolver.Resolve(c.FormType),
          c.Model.Label ?? L["Add"],
          c.Model.Color))
        .ToList();
    }

    var columns = CreateColumns();
    var formType = ResolveFormServiceType(columns);
    var formPath = ResolveFormServicePath(columns);
    if (formType != null && !string.IsNullOrEmpty(formPath))
    {
      return [new GridCreateDescriptor(formType, formPath, L[Feature.Label], UiColor.Orange)];
    }

    return [];
  }
}

public abstract class GridService<TEntity>(PlatformDbContext db, PlatformAppService? app = null) : GridService<TEntity, TEntity>(db, app)
  where TEntity : class, IEntity, new();
