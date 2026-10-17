using System.Linq.Expressions;
using Air.App.Components;
using Air.Data.Contracts;
using Air.Data.Internal;

namespace Air.App.Grid;

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
  internal object? StatusFlow { get; private set; }

  public void UseForm<TFormService>() where TFormService : class
  {
    FormServiceType = typeof(TFormService);
  }

  public void UseStatusOf<TEntity>(Screens.IRecordStatuses<TEntity> statuses) where TEntity : class
  {
    StatusFlow = statuses;
  }

  public GridColumn<TProp> Add<TRow, TProp>(Expression<Func<TRow, TProp>> expression)
  {
    var path = ToCamelPath(MemberPath.Of(expression));
    var col = new GridColumnDto
    {
      Path = path,
      FilterPath = path,
      Label = PathToLabel(path),
      Type = ResolveFieldType(typeof(TProp)),
      Cell = ResolveDefaultCell(typeof(TProp))
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
      Label = PathToLabel(path),
      Type = ResolveFieldType(typeof(TProp)),
      Cell = ResolveDefaultCell(typeof(TProp))
    };
    _columns.Add(col);
    return new GridColumn<TProp>(col, this);
  }

  private static string ToCamelPath(string path)
  {
    var parts = path.Split('.');
    for (int i = 0; i < parts.Length; i++)
    {
      if (parts[i].Length > 0)
      {
        parts[i] = char.ToLowerInvariant(parts[i][0]) + parts[i][1..];
      }
    }
    return string.Join('.', parts);
  }

  private static string PathToLabel(string path)
  {
    var last = path.Split('.').Last();
    if (last.EndsWith("Id", StringComparison.Ordinal) && last.Length > 2)
    {
      last = last[..^2];
    }
    if (last.Length > 0)
    {
      last = char.ToUpperInvariant(last[0]) + last[1..];
    }
    return System.Text.RegularExpressions.Regex.Replace(last, "([a-z])([A-Z])", "$1 $2");
  }

  private static string ResolveFieldType(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool)) return UiFieldType.Checkbox;
    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)) return UiFieldType.Date;
    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal) || underlying == typeof(double)) return UiFieldType.Number;
    return UiFieldType.Text;
  }

  private static CellModel ResolveDefaultCell(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool)) return new GridCellCheckboxModel();
    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)) return new GridCellDateModel();
    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal) || underlying == typeof(double)) return new GridCellNumberModel();
    return new GridCellTextModel();
  }
}

public class Columns<TRow> : Columns where TRow : class
{
  public GridColumn<TProp> Add<TProp>(Expression<Func<TRow, TProp>> expression) =>
    Add<TRow, TProp>(expression);

  public void UseStatusOf(Screens.IRecordStatuses<TRow> statuses) =>
    base.UseStatusOf(statuses);
}

public class GridColumn<TProp>(GridColumnDto dto, Columns owner)
{
  public GridColumnDto Dto => dto;

  public GridColumn<TProp> Label(string label) { dto.Label = label; return this; }
  public GridColumn<TProp> Width(int px) { dto.Width = px; return this; }
  public GridColumn<TProp> Width(string width)
  {
    dto.Width = int.TryParse(width.Replace("px", "").Trim(), out var px) ? px : 100;
    return this;
  }
  public GridColumn<TProp> Sortable(bool sortable = true) { dto.IsSortable = sortable; return this; }
  public GridColumn<TProp> Filterable(bool filterable = true) { dto.IsFilterable = filterable; return this; }
  public GridColumn<TProp> Computed() { dto.IsSortable = false; dto.IsFilterable = false; return this; }
  public GridColumn<TProp> Hidden(bool hidden = true) { dto.IsHidden = hidden; return this; }

  public GridColumn<TProp> AsOpenForm<TFormService>() where TFormService : class
  {
    dto.Cell = new GridCellOpenFormModel();
    owner.FormServiceType = typeof(TFormService);
    return this;
  }

  public GridColumn<TProp> AsCheckbox()
  {
    dto.Type = UiFieldType.Checkbox;
    dto.Cell = new GridCellCheckboxModel();
    return this;
  }

  public GridColumn<TProp> AsSelect(IEnumerable<OptionDto> options)
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellSelectModel();
    dto.Options = options.Select(o => new Reference { Id = o.Id, Name = o.Name }).ToList();
    return this;
  }

  public GridColumn<TProp> AsDate()
  {
    dto.Type = UiFieldType.Date;
    dto.Cell = new GridCellDateModel();
    return this;
  }

  public GridColumn<TProp> AsDatetime()
  {
    dto.Type = UiFieldType.Datetime;
    dto.Cell = new GridCellDatetimeModel();
    return this;
  }

  public GridColumn<TProp> AsNumber()
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellNumberModel();
    return this;
  }

  public GridColumn<TProp> AsMoney(string? currency = null)
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellMoneyModel { Currency = currency, Decimals = 2 };
    return this;
  }

  public GridColumn<TProp> AsPercent()
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellPercentModel { Decimals = 2 };
    return this;
  }

  public GridColumn<TProp> Sum()
  {
    return this;
  }

  public GridColumn<TProp> Stepper(bool hasStepper = true)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.HasStepper = hasStepper;
    return this;
  }

  public GridColumn<TProp> Step(double step)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.Step = step;
    return this;
  }

  public GridColumn<TProp> Min(double min)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.Min = min;
    return this;
  }

  public GridColumn<TProp> Max(double max)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.Max = max;
    return this;
  }

  public GridColumn<TProp> Editable(bool isEditable = true)
  {
    dto.IsEditable = isEditable;
    if (isEditable && dto.NumberEdit == null && dto.Cell is GridCellNumberModel or GridCellMoneyModel or GridCellPercentModel)
    {
      dto.NumberEdit = new GridNumberEditDto { Step = 1, HasStepper = false };
    }
    return this;
  }

  public GridColumn<TProp> AsReference<TGridService>()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellReferenceModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService));
    return this;
  }

  public GridColumn<TProp> AsReference<TGridService, TSource>()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellReferenceModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService));
    return this;
  }

  public GridColumn<TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceMember, TVal value)
  {
    return this;
  }

  public GridColumn<TProp> Hue(Func<object, string?> selector) => this;
  public GridColumn<TProp> Hue<TRow>(Func<TRow, string?> selector) => this;

  public GridColumn<TProp> Color(Func<TProp, string> colorSelector)
  {
    return this;
  }
  public GridColumn<TProp> Color<TEntity>(Expression<Func<TEntity, string?>> colorSelector) => this;
}

public class GridColumn<TEntity, TProp>(GridColumnDto dto, Columns owner) : GridColumn<TProp>(dto, owner)
{
  public new GridColumn<TEntity, TProp> Label(string label) { base.Label(label); return this; }
  public new GridColumn<TEntity, TProp> Width(int px) { base.Width(px); return this; }
  public new GridColumn<TEntity, TProp> Width(string width) { base.Width(width); return this; }
  public new GridColumn<TEntity, TProp> Sortable(bool sortable = true) { base.Sortable(sortable); return this; }
  public new GridColumn<TEntity, TProp> Filterable(bool filterable = true) { base.Filterable(filterable); return this; }
  public new GridColumn<TEntity, TProp> Computed() { base.Computed(); return this; }
  public new GridColumn<TEntity, TProp> Hidden(bool hidden = true) { base.Hidden(hidden); return this; }
  public new GridColumn<TEntity, TProp> AsOpenForm<TFormService>() where TFormService : class { base.AsOpenForm<TFormService>(); return this; }
  public new GridColumn<TEntity, TProp> AsCheckbox() { base.AsCheckbox(); return this; }
  public new GridColumn<TEntity, TProp> AsSelect(IEnumerable<OptionDto> options) { base.AsSelect(options); return this; }
  public new GridColumn<TEntity, TProp> AsDate() { base.AsDate(); return this; }
  public new GridColumn<TEntity, TProp> AsDatetime() { base.AsDatetime(); return this; }
  public new GridColumn<TEntity, TProp> AsNumber() { base.AsNumber(); return this; }
  public new GridColumn<TEntity, TProp> AsMoney(string? currency = null) { base.AsMoney(currency); return this; }
  public new GridColumn<TEntity, TProp> AsPercent() { base.AsPercent(); return this; }
  public new GridColumn<TEntity, TProp> Sum() { base.Sum(); return this; }
  public new GridColumn<TEntity, TProp> Stepper(bool hasStepper = true) { base.Stepper(hasStepper); return this; }
  public new GridColumn<TEntity, TProp> Step(double step) { base.Step(step); return this; }
  public new GridColumn<TEntity, TProp> Min(double min) { base.Min(min); return this; }
  public new GridColumn<TEntity, TProp> Max(double max) { base.Max(max); return this; }
  public new GridColumn<TEntity, TProp> Editable(bool isEditable = true) { base.Editable(isEditable); return this; }
  public new GridColumn<TEntity, TProp> AsReference<TGridService>() { base.AsReference<TGridService>(); return this; }
  public new GridReferenceColumn<TEntity, TSource, TProp> AsReference<TGridService, TSource>()
  {
    base.AsReference<TGridService, TSource>();
    return new GridReferenceColumn<TEntity, TSource, TProp>(Dto, owner);
  }
  public new GridColumn<TEntity, TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceMember, TVal value) { base.Where(sourceMember, value); return this; }
  public GridColumn<TEntity, TProp> Color(Expression<Func<TEntity, string?>> colorSelector) => this;
  public GridColumn<TEntity, TProp> Hue(Expression<Func<TEntity, string?>> hueSelector) => this;
}

public class GridReferenceColumn<TEntity, TSourceEntity, TProp>(GridColumnDto dto, Columns owner)
  : GridColumn<TEntity, TProp>(dto, owner)
{
  public GridReferenceColumn<TEntity, TSourceEntity, TProp> Where<TVal>(
    Expression<Func<TSourceEntity, TVal>> sourceMember,
    TVal value)
  {
    return this;
  }
}
