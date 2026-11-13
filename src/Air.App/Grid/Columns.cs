using Platform.App.Grid.Internals;
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
      Type = isRefType ? UiFieldType.Text : UiMemberDefaults.ResolveFieldType(typeof(TProp)),
      TemporalType = UiMemberDefaults.ResolveTemporalType(typeof(TProp)),
      Cell = isRefType ? new GridCellReferenceModel() : GridColumnDefaults.ResolveDefaultCell(typeof(TProp)),
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
      Type = isRefType ? UiFieldType.Text : UiMemberDefaults.ResolveFieldType(typeof(TProp)),
      TemporalType = UiMemberDefaults.ResolveTemporalType(typeof(TProp)),
      Cell = isRefType ? new GridCellReferenceModel() : GridColumnDefaults.ResolveDefaultCell(typeof(TProp)),
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
      Type = UiMemberDefaults.ResolveFieldType(typeof(TProp)),
      TemporalType = UiMemberDefaults.ResolveTemporalType(typeof(TProp)),
      Cell = GridColumnDefaults.ResolveDefaultCell(typeof(TProp)),
      MemberType = typeof(TProp)
    };
    _columns.Add(col);
    return new GridColumn<object, object, TProp>(col, this);
  }

  internal static string ToCamelPath(string path) => UiMemberDefaults.ToCamelPath(path);

  internal static string PathToLabel(string path, Type? ownerType = null) =>
    UiMemberDefaults.PathToLabel(path, ownerType);
}
