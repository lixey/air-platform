using System.Linq.Expressions;
using Air.App.Components;
using Air.Data.Contracts;
using Air.Data.Internal;

namespace Air.App.Grid;

public class Columns
{
  private readonly List<GridColumnDto> _columns = [];
  public IReadOnlyList<GridColumnDto> List => _columns;

  public Type? FormServiceType { get; set; }

  public void UseForm<TFormService>() where TFormService : class
  {
    FormServiceType = typeof(TFormService);
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
    return last.Length > 0 ? char.ToUpperInvariant(last[0]) + last[1..] : path;
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

public sealed class Columns<TRow> : Columns
{
  public GridColumn<TProp> Add<TProp>(Expression<Func<TRow, TProp>> expression) =>
    Add<TRow, TProp>(expression);
}

public sealed class GridColumn<TProp>(GridColumnDto dto, Columns owner)
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
    dto.Cell = new GridCellNumberModel();
    return this;
  }

  public GridColumn<TProp> AsPercent()
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellNumberModel();
    return this;
  }

  public GridColumn<TProp> Sum()
  {
    return this;
  }

  public GridColumn<TProp> Editable(bool isEditable = true)
  {
    dto.IsEditable = isEditable;
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

  public GridColumn<TProp> Color(Func<TProp, string> colorSelector)
  {
    return this;
  }
}
