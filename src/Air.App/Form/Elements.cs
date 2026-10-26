using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Form;

public class Elements
{
  private readonly List<FormElementDto> _elements = [];
  private readonly List<FormTabDto> _tabs = [];
  private string? _currentTab;

  private readonly List<ChildGridBuilder> _children = [];
  private readonly List<ReportTableDto> _tables = [];
  public IReadOnlyList<FormElementDto> List => _elements;
  public IReadOnlyList<FormTabDto> Tabs => _tabs;
  public IReadOnlyList<ChildGridBuilder> Children => _children;
  public IReadOnlyList<ReportTableDto> Tables => _tables;

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

    _tables.Add(table);
  }

  public void AddTab(string label, string? key = null, string? title = null)
  {
    var tabKey = key ?? label.ToLowerInvariant().Replace(' ', '-');
    _currentTab = tabKey;
    _tabs.Add(new FormTabDto { Key = tabKey, Label = label, Title = title });
  }

  public void AddSeparator(string? label = null)
  {
    if (_elements.Count > 0)
    {
      _elements[^1].HasSeparatorAfter = true;
      _elements[^1].SeparatorLabel = label;
    }
  }

  public ChildGridBuilder AddChild<TChildGridService>()
  {
    var child = new ChildGridBuilder(Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TChildGridService)), typeof(TChildGridService), _currentTab);
    _children.Add(child);
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

  public sealed class StatusFlowBuilder(Elements owner)
  {
    public StatusFlowBuilder AllowReadOnly(bool allow = true)
    {
      owner.StatusFlowAllowsReadOnly = allow;
      return this;
    }
  }

  public StatusFlowBuilder UseStatusOf<TEntity>(Screens.IRecordStatuses<TEntity> statuses) where TEntity : class
  {
    StatusFlow = statuses;
    return new StatusFlowBuilder(this);
  }

  private static readonly ConcurrentDictionary<(Type EntityType, string ExpressionKey), Delegate> CompiledInfoSelectors = new();

  public FormElement<TEntity, TProp> Add<TEntity, TProp>(Expression<Func<TEntity, TProp>> expression)
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

    if (memberPath.Contains('.'))
    {
      var navPaths = ExtractNavigationPaths(expression);
      if (navPaths.Count > 0)
      {
        el.InfoNavigationPaths = navPaths;
      }

      el.InfoValueGetter = obj => PropertyAccessorCache.GetPathValue(obj, memberPath);
    }

    _elements.Add(el);
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

    var compiled = Grid.GridColumn<object>.ContainsClosure(expression.Body)
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
      IsServerOwned = true,
      InfoNavigationPaths = navPaths.Count > 0 ? navPaths : null,
      InfoValueGetter = obj =>
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
      }
    };
    _elements.Add(el);
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
    while (_elements.Any(e => string.Equals(e.Path, candidate, StringComparison.OrdinalIgnoreCase)))
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
    var el = new FormElementDto
    {
      Path = path,
      Label = PathToLabel(path, null),
      Type = ResolveFieldType(typeof(TProp)),
      TemporalType = ResolveTemporalType(typeof(TProp)),
      Model = ResolveDefaultModel(typeof(TProp)),
      Tab = _currentTab,
      IsDisabled = _disabled
    };
    _elements.Add(el);
    return new FormElement<object, TProp>(el);
  }

  internal static string ToCamelPath(string path)
  {
    var parts = path.Split('.');
    for (var i = 0; i < parts.Length; i++)
    {
      if (parts[i].Length > 0)
      {
        parts[i] = char.ToLowerInvariant(parts[i][0]) + parts[i][1..];
      }
    }

    return string.Join('.', parts);
  }

  internal static string PathToLabel(string path, Type? ownerType = null)
  {
    var last = path.Split('.').Last();
    if (last.EndsWith("Id", StringComparison.Ordinal) && last.Length > 2)
    {
      var navCandidate = last[..^2];
      if (ownerType?.GetProperty(navCandidate, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) != null)
      {
        last = navCandidate;
      }
    }

    if (last.Length > 0)
    {
      last = char.ToUpperInvariant(last[0]) + last[1..];
    }

    return System.Text.RegularExpressions.Regex.Replace(last, "([a-z0-9])([A-Z])", "$1 $2");
  }

  internal static string ResolveFieldType(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool))
    {
      return UiFieldType.Checkbox;
    }

    if (underlying == typeof(DateOnly))
    {
      return UiFieldType.Date;
    }

    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
    {
      return UiFieldType.Datetime;
    }

    if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
    {
      return UiFieldType.Time;
    }

    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte) ||
        underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
    {
      return UiFieldType.Number;
    }

    return UiFieldType.Text;
  }

  internal static string? ResolveTemporalType(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(DateOnly))
    {
      return UiTemporalType.DateOnly;
    }

    if (underlying == typeof(DateTime))
    {
      return UiTemporalType.DateTime;
    }

    if (underlying == typeof(DateTimeOffset))
    {
      return UiTemporalType.DateTimeOffset;
    }

    return null;
  }

  internal static FormElementModel ResolveDefaultModel(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool))
    {
      return new FormCheckboxModel();
    }

    if (underlying == typeof(DateOnly))
    {
      return new FormDateModel();
    }

    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
    {
      return new FormDatetimeModel();
    }

    if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
    {
      return new FormTimeModel();
    }

    if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
    {
      return new FormNumberModel { Step = 0.01 };
    }

    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte))
    {
      return new FormNumberModel { Step = 1.0 };
    }

    return new FormInputModel();
  }
}
