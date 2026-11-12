using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Grid;

public class Columns
{
  private readonly List<GridColumnDto> _columns = [];
  public IReadOnlyList<GridColumnDto> List => _columns;

  public void RetainOnly(IEnumerable<string> paths)
  {
    var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
    _columns.RemoveAll(c => c.Path == null || !set.Contains(c.Path));
  }

  public Type? FormServiceType { get; set; }
  public Type? CoverFileGridType { get; set; }
  internal object? StatusFlow { get; private set; }
  internal bool StatusFlowAllowsReadOnly { get; private set; }

  public sealed class StatusFlowBuilder(Columns owner)
  {
    public StatusFlowBuilder AllowReadOnly(bool allow = true)
    {
      owner.StatusFlowAllowsReadOnly = allow;
      return this;
    }
  }

  public void UseForm<TFormService>() where TFormService : class
  {
    FormServiceType = typeof(TFormService);
  }

  public StatusFlowBuilder UseStatusOf<TEntity>(Screens.IRecordStatuses<TEntity> statuses) where TEntity : class
  {
    StatusFlow = statuses;
    return new StatusFlowBuilder(this);
  }

  public GridColumn<TRow, TRow, TProp> Add<TRow, TProp>(Expression<Func<TRow, TProp?>> expression)
  {
    var path = ToCamelPath(MemberPath.Of(expression));
    var isRefType = typeof(Reference).IsAssignableFrom(typeof(TProp));
    var col = new GridColumnDto
    {
      Path = path,
      FilterPath = isRefType && !path.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? path + "Id" : path,
      Label = PathToLabel(path, typeof(TRow)),
      Type = isRefType ? UiFieldType.Text : ResolveFieldType(typeof(TProp)),
      TemporalType = Form.Elements.ResolveTemporalType(typeof(TProp)),
      Cell = isRefType ? new GridCellReferenceModel() : ResolveDefaultCell(typeof(TProp)),
      Header = isRefType ? new GridHeaderReferenceModel() : null,
      MemberType = typeof(TProp)
    };
    _columns.Add(col);
    return new GridColumn<TRow, TRow, TProp>(col, this);
  }

  private static readonly ConcurrentDictionary<(Type RowType, string ExpressionKey), Func<object, object?>> CompiledValueEvaluators = new();

  public GridColumn<TRow, TRow, TProp> Add<TRow, TProp>(string path, Expression<Func<TRow, TProp>> expression)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(path);
    ArgumentNullException.ThrowIfNull(expression);

    var camelPath = ToCamelPath(path);
    var isRefType = typeof(Reference).IsAssignableFrom(typeof(TProp));
    var isSubquery = ExpressionClosureInspector.ContainsClosure(expression.Body);

    Func<object, object?>? evaluator = null;
    if (!isSubquery)
    {
      var exprKey = expression.ToString();
      evaluator = CompiledValueEvaluators.GetOrAdd(
        (typeof(TRow), exprKey),
        _ => BuildSafeEvaluator(expression));
    }

    var col = new GridColumnDto
    {
      Path = camelPath,
      FilterPath = isRefType && !camelPath.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? camelPath + "Id" : camelPath,
      Label = PathToLabel(camelPath, typeof(TRow)),
      Type = isRefType ? UiFieldType.Text : ResolveFieldType(typeof(TProp)),
      TemporalType = Form.Elements.ResolveTemporalType(typeof(TProp)),
      Cell = isRefType ? new GridCellReferenceModel() : ResolveDefaultCell(typeof(TProp)),
      Header = isRefType ? new GridHeaderReferenceModel() : null,
      MemberType = typeof(TProp),
      ValueExpression = expression,
      ValueEvaluator = evaluator,
      IsSubqueryExpression = isSubquery
    };
    _columns.Add(col);
    return new GridColumn<TRow, TRow, TProp>(col, this);
  }

  private static Func<object, object?> BuildSafeEvaluator<TRow, TProp>(Expression<Func<TRow, TProp>> expression)
  {
    var compiled = expression.Compile();
    return obj =>
    {
      if (obj is TRow typed)
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
  }

  public GridColumn<object, object, TProp> Add<TProp>(string path)
  {
    var camelPath = ToCamelPath(path);
    var col = new GridColumnDto
    {
      Path = camelPath,
      FilterPath = camelPath,
      Label = PathToLabel(camelPath, null),
      Type = ResolveFieldType(typeof(TProp)),
      TemporalType = Form.Elements.ResolveTemporalType(typeof(TProp)),
      Cell = ResolveDefaultCell(typeof(TProp)),
      MemberType = typeof(TProp)
    };
    _columns.Add(col);
    return new GridColumn<object, object, TProp>(col, this);
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
    var parts = path.Split('.');
    var last = parts[^1];
    if (parts.Length >= 2 &&
        (string.Equals(last, "name", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(last, "title", StringComparison.OrdinalIgnoreCase)))
    {
      last = parts[^2];
    }

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

    return Regex.Replace(last, "([a-z0-9])([A-Z])", "$1 $2");
  }

  private static string ResolveFieldType(Type type)
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
      return UiFieldType.DateTime;
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

  private static CellModel ResolveDefaultCell(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool))
    {
      return new GridCellCheckboxModel();
    }

    if (underlying == typeof(DateOnly))
    {
      return new GridCellDateModel();
    }

    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
    {
      return new GridCellDateTimeModel();
    }

    if (underlying == typeof(TimeOnly) || underlying == typeof(TimeSpan))
    {
      return new GridCellTimeModel();
    }

    if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
    {
      return new GridCellNumberModel { Decimals = 2, IsGrouped = true };
    }

    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(short) || underlying == typeof(byte))
    {
      return new GridCellNumberModel { Decimals = 0, IsGrouped = true };
    }

    return new GridCellTextModel();
  }
}
