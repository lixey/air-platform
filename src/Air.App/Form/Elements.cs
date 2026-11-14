using Platform.App.Form.Internals;
using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Form;

public class Elements
{
  private string? _currentTab;

  public List<FormElementDto> Items { get; } = [];
  public List<FormTabDto> TabItems { get; } = [];
  public List<ChildGridBuilder> ChildItems { get; } = [];
  public List<ReportTableDto> TableItems { get; } = [];

  public IReadOnlyList<FormElementDto> List => Items;
  public IReadOnlyList<FormTabDto> Tabs => TabItems;
  public IReadOnlyList<ChildGridBuilder> Children => ChildItems;
  public IReadOnlyList<ReportTableDto> Tables => TableItems;
  public int Count => Items.Count;
  public FormElementDto this[int index] => Items[index];

  public FormElementDto? Find(string path) =>
    Items.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

  public static (Type? ReferenceServiceType, Type? CreateFormServiceType) GetReferenceBinding(FormElementDto element)
  {
    var binding = FormElementBinding.TryGet(element);
    return (binding?.ReferenceServiceType, binding?.CreateFormServiceType);
  }

  public bool Remove(string path) =>
    Items.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) > 0;

  public int RemoveAll(Predicate<FormElementDto> match) => Items.RemoveAll(match);

  public void Clear()
  {
    Items.Clear();
    TabItems.Clear();
    ChildItems.Clear();
    TableItems.Clear();
    _currentTab = null;
  }

  public void AddReportTable(ReportTableDto table)
  {
    if (string.IsNullOrEmpty(table.Tab) && _currentTab != null)
    {
      table = new ReportTableDto
      {
        Key = table.Key,
        Title = table.Title,
        Subtitle = table.Subtitle,
        Tab = _currentTab,
        Searchable = table.Searchable,
        Sortable = table.Sortable,
        Compact = table.Compact,
        MaxHeight = table.MaxHeight,
        EmptyText = table.EmptyText,
        FilterField = table.FilterField,
        FilterChips = table.FilterChips,
        Columns = table.Columns,
        Rows = table.Rows,
        BottomRows = table.BottomRows
      };
    }

    TableItems.Add(table);
  }

  public void AddTab(string label, string? key = null, string? title = null)
  {
    var tabKey = key ?? label.ToLowerInvariant().Replace(' ', '-');
    _currentTab = tabKey;
    TabItems.Add(new FormTabDto { Key = tabKey, Label = label, Title = title });
  }

  public void AddTab(string label, string? key, string uiSource, string dbSource) =>
    AddTab(label, key, Feature.FormatSourceTitle(uiSource, dbSource));

  public void AddSeparator(string? label = null)
  {
    if (Items.Count > 0)
    {
      Items[^1].HasSeparatorAfter = true;
      Items[^1].SeparatorLabel = label;
    }
  }

  public ChildGridBuilder AddChild<TChildGridService>()
  {
    var child = new ChildGridBuilder(ServicePathResolver.Resolve(typeof(TChildGridService)), typeof(TChildGridService), _currentTab);
    ChildItems.Add(child);
    return child;
  }

  protected ChildGridBuilder<TParent, TChild> AddTypedChild<TChildGridService, TParent, TChild>()
    where TParent : class, IEntity
    where TChild : class, IEntity
  {
    var child = new ChildGridBuilder<TParent, TChild>(ServicePathResolver.Resolve(typeof(TChildGridService)), typeof(TChildGridService), _currentTab);
    ChildItems.Add(child);
    return child;
  }

  private bool _disabled;
  internal Type? RecordLabelGridType { get; private set; }

  public void UseRecordLabelOf<TGridService>()
  {
    RecordLabelGridType = typeof(TGridService);
  }

  public void Disable(bool disabled = true)
  {
    _disabled = disabled;
  }

  internal object? StatusFlow { get; private set; }
  internal bool StatusFlowAllowsReadOnly { get; private set; }

  public StatusFlowOptions UseStatusOf<TEntity>(IRecordStatuses<TEntity> statuses) where TEntity : class
  {
    StatusFlow = statuses;
    return new StatusFlowOptions(allow => StatusFlowAllowsReadOnly = allow);
  }

  private static readonly ConcurrentDictionary<(Type EntityType, string ExpressionKey), Delegate> CompiledInfoSelectors = new();

  public FormElement<TEntity, TProp> Add<TEntity, TProp>(Expression<Func<TEntity, TProp?>> expression)
  {
    var memberPath = MemberPath.Of(expression);
    var path = ToCamelPath(memberPath);
    var el = new FormElementDto
    {
      Path = path,
      Label = PathToLabel(path, typeof(TEntity)),
      Type = ResolveFieldType(typeof(TProp)),
      TemporalType = ResolveTemporalType(typeof(TProp)),
      Model = ResolveDefaultModel(typeof(TProp)),
      Tab = _currentTab,
      IsDisabled = _disabled
    };
    var binding = FormElementBinding.Of(el);
    binding.ResolvedPropertyType = typeof(TProp);

    if (memberPath.Contains('.'))
    {
      el.IsDisabled = true;
      el.IsServerOwned = true;
      var navPaths = ExtractNavigationPaths(expression);
      if (navPaths.Count > 0)
      {
        binding.InfoNavigationPaths = navPaths;
      }

      binding.InfoValueGetter = obj => PropertyAccessorCache.GetPathValue(obj, memberPath);
    }
    else if (!string.IsNullOrEmpty(memberPath) && typeof(IEntity).IsAssignableFrom(typeof(TProp)))
    {
      binding.NavigationPropertyName = memberPath;
      el.ReferencePath = path;
      var navProp = PropertyAccessorCache.GetProperty(typeof(TEntity), memberPath);
      var explicitFkName = navProp?.GetCustomAttribute<ForeignKeyAttribute>()?.Name;
      var fkProp = PropertyAccessorCache.GetProperty(typeof(TEntity), explicitFkName ?? memberPath + "Id") ?? throw new InvalidOperationException(
          $"Navigation property '{typeof(TEntity).Name}.{memberPath}' requires a matching foreign key property '{memberPath}Id' (or [ForeignKey]) to be used in Elements.Add.");
      el.Path = ToCamelPath(fkProp.Name);
      el.Label = PathToLabel(path, typeof(TEntity));
      el.Type = ResolveFieldType(fkProp.PropertyType);
      el.Model = ResolveDefaultModel(fkProp.PropertyType);
      binding.ResolvedPropertyType = fkProp.PropertyType;
    }

    Items.Add(el);
    return new FormElement<TEntity, TProp>(el);
  }

  public FormElement<TEntity, TProp> AddInfo<TEntity, TProp>(string label, Expression<Func<TEntity, TProp>> expression, string? key = null)
  {
    var navPaths = ExtractNavigationPaths(expression);
    var exprKey = expression.ToString();
    var basePath = !string.IsNullOrWhiteSpace(key)
      ? ToCamelPath(key)
      : BuildStableInfoPath(expression, exprKey);
    var path = EnsureUniquePath(basePath);

    var compiled = ExpressionClosureInspector.ContainsClosure(expression.Body)
      ? expression.Compile()
      : (Func<TEntity, TProp>)CompiledInfoSelectors.GetOrAdd(
          (typeof(TEntity), exprKey),
          _ => expression.Compile());

    var el = new FormElementDto
    {
      Path = path,
      Label = label,
      Type = ResolveFieldType(typeof(TProp)),
      TemporalType = ResolveTemporalType(typeof(TProp)),
      Model = new FormInfoModel(),
      Tab = _currentTab,
      IsDisabled = true,
      IsServerOwned = true
    };
    var binding = FormElementBinding.Of(el);
    binding.HasExplicitLabel = true;
    binding.ResolvedPropertyType = typeof(TProp);
    binding.InfoNavigationPaths = navPaths.Count > 0 ? navPaths : null;
    binding.InfoValueGetter = obj =>
    {
      if (obj is TEntity typed)
      {
        try
        {
          return compiled(typed);
        }
        catch (NullReferenceException)
        {
          return null;
        }
      }

      return null;
    };
    Items.Add(el);
    return new FormElement<TEntity, TProp>(el);
  }

  private static string BuildStableInfoPath<TEntity, TProp>(Expression<Func<TEntity, TProp>> expression, string exprKey)
  {
    var memberPath = MemberPath.Of(expression);
    if (!string.IsNullOrEmpty(memberPath))
    {
      return "info_" + ToCamelPath(memberPath).Replace('.', '_');
    }

    var chars = exprKey.Where(char.IsLetterOrDigit).Take(24).ToArray();
    return chars.Length > 0 ? "info_" + new string(chars) : "info";
  }

  private string EnsureUniquePath(string basePath)
  {
    var candidate = basePath;
    var suffix = 2;
    while (Items.Any(e => string.Equals(e.Path, candidate, StringComparison.OrdinalIgnoreCase)))
    {
      candidate = $"{basePath}_{suffix++}";
    }

    return candidate;
  }

  internal static List<string> ExtractNavigationPaths(LambdaExpression lambda)
  {
    var navs = new HashSet<string>(StringComparer.Ordinal);
    if (lambda.Parameters.Count > 0)
    {
      CollectNavigationPaths(lambda.Body, lambda.Parameters[0], navs);
    }

    return [.. navs];
  }

  private static void CollectNavigationPaths(Expression? expr, ParameterExpression rootParam, HashSet<string> navs)
  {
    if (expr == null)
    {
      return;
    }

    if (expr is MemberExpression me)
    {
      var parts = new List<string>(4);
      Expression? cur = me;
      while (cur is MemberExpression m)
      {
        parts.Add(m.Member.Name);
        cur = m.Expression;
      }

      if (cur == rootParam)
      {
        if (parts.Count > 1)
        {
          parts.Reverse();
          navs.Add(string.Join('.', parts.Take(parts.Count - 1)));
        }

        return;
      }

      CollectNavigationPaths(me.Expression, rootParam, navs);
      return;
    }

    switch (expr)
    {
      case UnaryExpression u:
        CollectNavigationPaths(u.Operand, rootParam, navs);
        break;
      case BinaryExpression b:
        CollectNavigationPaths(b.Left, rootParam, navs);
        CollectNavigationPaths(b.Right, rootParam, navs);
        break;
      case ConditionalExpression c:
        CollectNavigationPaths(c.Test, rootParam, navs);
        CollectNavigationPaths(c.IfTrue, rootParam, navs);
        CollectNavigationPaths(c.IfFalse, rootParam, navs);
        break;
      case MethodCallExpression call:
        CollectNavigationPaths(call.Object, rootParam, navs);
        foreach (var arg in call.Arguments)
        {
          CollectNavigationPaths(arg, rootParam, navs);
        }

        break;
      case NewArrayExpression arr:
        foreach (var item in arr.Expressions)
        {
          CollectNavigationPaths(item, rootParam, navs);
        }

        break;
      case NewExpression n:
        foreach (var arg in n.Arguments)
        {
          CollectNavigationPaths(arg, rootParam, navs);
        }

        break;
      case InvocationExpression inv:
        CollectNavigationPaths(inv.Expression, rootParam, navs);
        foreach (var arg in inv.Arguments)
        {
          CollectNavigationPaths(arg, rootParam, navs);
        }

        break;
    }
  }

  public FormElement<object, TProp> Add<TProp>(string path)
  {
    var isDotPath = path.Contains('.');
    var el = new FormElementDto
    {
      Path = path,
      Label = PathToLabel(path, null),
      Type = ResolveFieldType(typeof(TProp)),
      TemporalType = ResolveTemporalType(typeof(TProp)),
      Model = ResolveDefaultModel(typeof(TProp)),
      Tab = _currentTab,
      IsDisabled = _disabled || isDotPath,
      IsServerOwned = isDotPath
    };
    FormElementBinding.Of(el).ResolvedPropertyType = typeof(TProp);
    Items.Add(el);
    return new FormElement<object, TProp>(el);
  }

  internal static string ToCamelPath(string path) => UiMemberDefaults.ToCamelPath(path);

  internal static string PathToLabel(string path, Type? ownerType = null) =>
    UiMemberDefaults.PathToLabel(path, ownerType);

  internal static string ResolveFieldType(Type type) => UiMemberDefaults.ResolveFieldType(type);

  internal static string? ResolveTemporalType(Type type) => UiMemberDefaults.ResolveTemporalType(type);

  internal static FormElementModel ResolveDefaultModel(Type type) =>
    FormElementDefaults.ResolveDefaultModel(type);
}
