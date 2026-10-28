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

  public GridColumn<TProp> Add<TRow, TProp>(Expression<Func<TRow, TProp>> expression)
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
    return new GridColumn<TProp>(col, this);
  }

  public GridColumn<TProp> Add<TProp>(string path)
  {
    var col = new GridColumnDto
    {
      Path = path,
      FilterPath = path,
      Label = PathToLabel(path, null),
      Type = ResolveFieldType(typeof(TProp)),
      TemporalType = Form.Elements.ResolveTemporalType(typeof(TProp)),
      Cell = ResolveDefaultCell(typeof(TProp)),
      MemberType = typeof(TProp)
    };
    _columns.Add(col);
    return new GridColumn<TProp>(col, this);
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
      return new GridCellDatetimeModel();
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
