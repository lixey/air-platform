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

    return System.Text.RegularExpressions.Regex.Replace(last, "([a-z0-9])([A-Z])", "$1 $2");
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

public class Columns<TRow> : Columns where TRow : class
{
  public GridColumn<TRow, TProp> Add<TProp>(Expression<Func<TRow, TProp>> expression)
  {
    var baseCol = base.Add<TRow, TProp>(expression);
    return new GridColumn<TRow, TProp>(baseCol.Dto, this);
  }

  public StatusFlowBuilder UseStatusOf(Screens.IRecordStatuses<TRow> statuses) =>
    base.UseStatusOf(statuses);
}

public class GridColumn<TProp>(GridColumnDto dto, Columns owner)
{
  private bool _hasExplicitLabel;
  public GridColumnDto Dto => dto;

  public GridColumn<TProp> Label(string label)
  {
    _hasExplicitLabel = true;
    dto.Label = label;
    return this;
  }
  public GridColumn<TProp> Title(string title)
  {
    dto.Title = title;
    return this;
  }
  public GridColumn<TProp> Title(string legacyUi, string legacyDb)
  {
    dto.Title = $"Легасі UI: {legacyUi} | БД: {legacyDb}";
    return this;
  }
  public GridColumn<TProp> Width(int px)
  {
    dto.Width = px;
    return this;
  }
  public GridColumn<TProp> Width(string width)
  {
    dto.Width = int.TryParse(width.Replace("px", "").Trim(), out var px) ? px : 100;
    return this;
  }
  public GridColumn<TProp> Sortable(bool sortable = true)
  {
    dto.IsSortable = sortable;
    return this;
  }
  public GridColumn<TProp> Filterable(bool filterable = true)
  {
    dto.IsFilterable = filterable;
    return this;
  }
  public GridColumn<TProp> Computed()
  {
    dto.IsComputed = true;
    dto.IsSortable = false;
    dto.IsFilterable = false;
    return this;
  }
  public GridColumn<TProp> Hidden(bool hidden = true)
  {
    dto.IsHidden = hidden;
    return this;
  }
  public GridColumn<TProp> Cell(CellModel cell)
  {
    dto.Cell = cell;
    return this;
  }

  private void StripDefaultIdLabel()
  {
    if (!_hasExplicitLabel && dto.Label is { } lbl && lbl.EndsWith(" Id", StringComparison.Ordinal) && lbl.Length > 3)
    {
      dto.Label = lbl[..^3];
    }
  }

  public GridColumn<TProp> AsOpenForm<TFormService>() where TFormService : class
  {
    dto.Cell = new GridCellOpenFormModel();
    owner.FormServiceType = typeof(TFormService);
    return this;
  }

  public GridColumn<TProp> AsCover<TFileGrid>() where TFileGrid : class
  {
    dto.Path = (dto.Path ?? "id") + "Cover";
    dto.Label = "Cover";
    dto.IsSortable = false;
    dto.IsFilterable = false;
    dto.Cell = new Grid.AddOns.Covers.Contracts.GridCellCoverModel
    {
      Service = Air.App.Modules.ServicePathResolver.Resolve(typeof(TFileGrid)),
      ParentService = owner.FormServiceType != null ? Air.App.Modules.ServicePathResolver.Resolve(owner.FormServiceType) : string.Empty
    };
    owner.CoverFileGridType = typeof(TFileGrid);
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
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellSelectModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.Options = options.Select(o => new OptionDto
    {
      Id = o.Id,
      Name = Localizer.Instance[o.Name],
      Color = o.Color,
      Title = o.Title != null ? Localizer.Instance[o.Title] : null
    }).ToList();
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
    dto.IsSummed = true;
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

  public GridColumn<TProp> Editable(Func<bool> condition) =>
    Editable(condition());

  public GridColumn<TProp> PickedBy<TSource>(Delegate method, int minLength = 2)
  {
    dto.Cell = new GridCellPickerModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TSource)),
      Method = method.Method.Name,
      MinLength = minLength
    };
    return this;
  }

  public GridColumn<TProp> AsReference<TGridService>()
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellReferenceModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService));
    if (dto.Path != null && !dto.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
      dto.FilterPath = dto.Path + "Id";
    }

    return this;
  }

  public GridColumn<TProp> AsReference<TGridService, TSource>()
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellReferenceModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService));
    if (dto.Path != null && !dto.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
      dto.FilterPath = dto.Path + "Id";
    }

    return this;
  }

  public GridColumn<TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceMember, TVal value)
  {
    var path = Columns.ToCamelPath(MemberPath.Of(sourceMember));
    var rule = FilterNode.Rule(path, FilterOperator.Equals, value);
    dto.ReferenceFilter = CombineFilter(dto.ReferenceFilter, rule);
    return this;
  }

  internal static FilterNode CombineFilter(FilterNode? existing, FilterNode addition)
  {
    if (existing is null)
    {
      return addition;
    }

    if (existing is FilterGroup { Logic: FilterLogic.And } group)
    {
      group.Items.Add(addition);
      return group;
    }

    return FilterNode.All(existing, addition);
  }

  public GridColumn<TProp> AsPhone()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellPhoneModel();
    return this;
  }

  public GridColumn<TProp> AsEmail()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellEmailModel();
    return this;
  }

  public GridColumn<TProp> AsUrl()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellUrlModel();
    return this;
  }

  public GridColumn<TProp> Hue(Func<object, string?> selector)
  {
    dto.ColorEvaluator = selector;
    ApplyColorPath();
    return this;
  }

  public GridColumn<TProp> Hue<TRow>(Func<TRow, string?> selector)
  {
    dto.ColorEvaluator = obj => obj is TRow row ? selector(row) : null;
    ApplyColorPath();
    return this;
  }

  public GridColumn<TProp> Color(Func<TProp, string> colorSelector)
  {
    dto.ColorEvaluator = obj => obj is TProp val ? colorSelector(val) : null;
    ApplyColorPath();
    return this;
  }

  public GridColumn<TProp> Color<TEntity>(Expression<Func<TEntity, string?>> colorSelector)
  {
    ApplyColorSelector(colorSelector);
    return this;
  }

  internal void ApplyColorSelector<TEntity>(Expression<Func<TEntity, string?>> colorSelector)
  {
    var compiled = colorSelector.Compile();
    dto.ColorExpression = colorSelector;
    dto.ColorEvaluator = obj =>
    {
      if (obj is TEntity entity)
      {
        try
        {
          return compiled(entity);
        }
        catch
        {
          return null;
        }
      }

      return null;
    };
    dto.ColorNavPath = ExtractNavigationPath(colorSelector.Body);
    ApplyColorPath();
  }

  private void ApplyColorPath()
  {
    var isRef = dto.Cell is GridCellReferenceModel || dto.ReferenceService != null || typeof(Reference).IsAssignableFrom(dto.MemberType);
    dto.Cell.ColorPath = isRef ? $"{dto.Path}.color" : $"{dto.Path}Color";
  }

  internal static string? ExtractNavigationPath(Expression expr)
  {
    var parts = new List<string>();
    var current = expr;
    while (current is MemberExpression me)
    {
      parts.Add(me.Member.Name);
      current = me.Expression;
    }

    if (parts.Count > 1)
    {
      parts.Reverse();
      return string.Join('.', parts.Take(parts.Count - 1));
    }

    return null;
  }
}

public class GridColumn<TEntity, TProp>(GridColumnDto dto, Columns owner) : GridColumn<TProp>(dto, owner)
{
  public new GridColumn<TEntity, TProp> Label(string label)
  {
    base.Label(label);
    return this;
  }
  public new GridColumn<TEntity, TProp> Title(string title)
  {
    base.Title(title);
    return this;
  }
  public new GridColumn<TEntity, TProp> Title(string legacyUi, string legacyDb)
  {
    base.Title(legacyUi, legacyDb);
    return this;
  }
  public new GridColumn<TEntity, TProp> Width(int px)
  {
    base.Width(px);
    return this;
  }
  public new GridColumn<TEntity, TProp> Width(string width)
  {
    base.Width(width);
    return this;
  }
  public new GridColumn<TEntity, TProp> Sortable(bool sortable = true)
  {
    base.Sortable(sortable);
    return this;
  }
  public new GridColumn<TEntity, TProp> Filterable(bool filterable = true)
  {
    base.Filterable(filterable);
    return this;
  }
  public new GridColumn<TEntity, TProp> Computed()
  {
    base.Computed();
    return this;
  }
  public new GridColumn<TEntity, TProp> Hidden(bool hidden = true)
  {
    base.Hidden(hidden);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsOpenForm<TFormService>() where TFormService : class
  {
    base.AsOpenForm<TFormService>();
    return this;
  }
  public GridColumn<TEntity, TProp> AsOpenForm<TFormService>(Expression<Func<TEntity, object?>> keySelector) where TFormService : class
  {
    Dto.Cell = new GridCellOpenFormModel
    {
      KeyPath = Columns.ToCamelPath(MemberPath.Of(keySelector))
    };
    owner.FormServiceType = typeof(TFormService);
    return this;
  }
  public GridColumn<TEntity, TProp> OpenForm(Func<TEntity, Type?> resolver)
  {
    Dto.Cell = new GridCellOpenFormModel();
    Dto.Width ??= 80;
    Dto.IsSticky = true;
    Dto.FormServiceResolver = row => row is TEntity typed ? resolver(typed) : null;
    return this;
  }
  public new GridColumn<TEntity, TProp> AsCheckbox()
  {
    base.AsCheckbox();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsSelect(IEnumerable<OptionDto> options)
  {
    base.AsSelect(options);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsDate()
  {
    base.AsDate();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsDatetime()
  {
    base.AsDatetime();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsNumber()
  {
    base.AsNumber();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsMoney(string? currency = null)
  {
    base.AsMoney(currency);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsPercent()
  {
    base.AsPercent();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsPhone()
  {
    base.AsPhone();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsEmail()
  {
    base.AsEmail();
    return this;
  }
  public new GridColumn<TEntity, TProp> AsUrl()
  {
    base.AsUrl();
    return this;
  }
  public new GridColumn<TEntity, TProp> Sum()
  {
    base.Sum();
    return this;
  }
  public new GridColumn<TEntity, TProp> Stepper(bool hasStepper = true)
  {
    base.Stepper(hasStepper);
    return this;
  }
  public new GridColumn<TEntity, TProp> Step(double step)
  {
    base.Step(step);
    return this;
  }
  public new GridColumn<TEntity, TProp> Min(double min)
  {
    base.Min(min);
    return this;
  }
  public new GridColumn<TEntity, TProp> Max(double max)
  {
    base.Max(max);
    return this;
  }
  public new GridColumn<TEntity, TProp> Editable(bool isEditable = true)
  {
    base.Editable(isEditable);
    return this;
  }
  public new GridColumn<TEntity, TProp> Editable(Func<bool> condition)
  {
    base.Editable(condition);
    return this;
  }
  public new GridColumn<TEntity, TProp> PickedBy<TSource>(Delegate method, int minLength = 2)
  {
    base.PickedBy<TSource>(method, minLength);
    return this;
  }
  public new GridColumn<TEntity, TProp> AsCover<TFileGrid>() where TFileGrid : class
  {
    base.AsCover<TFileGrid>();
    return this;
  }
  public new GridReferenceColumn<TEntity, TProp, TProp> AsReference<TGridService>()
  {
    base.AsReference<TGridService>();
    return new GridReferenceColumn<TEntity, TProp, TProp>(Dto, owner);
  }
  public new GridReferenceColumn<TEntity, TSource, TProp> AsReference<TGridService, TSource>()
  {
    base.AsReference<TGridService, TSource>();
    return new GridReferenceColumn<TEntity, TSource, TProp>(Dto, owner);
  }
  public new GridColumn<TEntity, TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceMember, TVal value)
  {
    base.Where(sourceMember, value);
    return this;
  }
  public GridColumn<TEntity, TProp> FilterBy<TFilterVal>(Expression<Func<TEntity, TFilterVal>> filterExpression)
  {
    Dto.FilterExpression = filterExpression;
    return this;
  }
  public GridColumn<TEntity, TProp> Color(Expression<Func<TEntity, string?>> colorSelector)
  {
    ApplyColorSelector(colorSelector);
    return this;
  }

  public GridColumn<TEntity, TProp> Hue(Expression<Func<TEntity, string?>> hueSelector) => Color(hueSelector);
}

public class GridColumn<TEntity, TRow, TProp>(GridColumnDto dto, Columns owner) : GridColumn<TEntity, TProp>(dto, owner)
{
  public GridColumn<TEntity, TRow, TProp> OpenForm(Func<TRow, Type?> resolver)
  {
    Dto.Cell = new GridCellOpenFormModel();
    Dto.Width ??= 80;
    Dto.IsSticky = true;
    Dto.FormServiceResolver = row => row is TRow typed ? resolver(typed) : null;
    return this;
  }
}

public class GridReferenceColumn<TEntity, TSourceEntity, TProp>(GridColumnDto dto, Columns owner)
  : GridColumn<TEntity, TProp>(dto, owner)
{
  public GridReferenceColumn<TEntity, TSourceEntity, TProp> Where<TVal>(
    Expression<Func<TSourceEntity, TVal>> sourceMember,
    TVal value)
  {
    base.Where(sourceMember, value);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> FilterBy<TFilterVal>(
    Expression<Func<TEntity, TFilterVal>> filterExpression)
  {
    Dto.FilterExpression = filterExpression;
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Hidden(bool hidden = true)
  {
    base.Hidden(hidden);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Label(string label)
  {
    base.Label(label);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Title(string title)
  {
    base.Title(title);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Title(string legacyUi, string legacyDb)
  {
    base.Title(legacyUi, legacyDb);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Color(Expression<Func<TEntity, string?>> colorSelector)
  {
    base.Color(colorSelector);
    return this;
  }
  public new GridReferenceColumn<TEntity, TSourceEntity, TProp> Hue(Expression<Func<TEntity, string?>> hueSelector)
  {
    base.Color(hueSelector);
    return this;
  }
}
