namespace Platform.App.Grid;

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
  public GridColumn<TProp> Disabled(bool disabled = true)
  {
    dto.IsDisabled = disabled;
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
    dto.Cell = new GridCellCoverModel
    {
      Service = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TFileGrid)),
      ParentService = owner.FormServiceType != null ? Platform.Api.Modules.ServicePathResolver.Resolve(owner.FormServiceType) : string.Empty
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

  public GridColumn<TProp> Decimals(int decimals)
  {
    switch (dto.Cell)
    {
      case GridCellNumberModel num:
        num.Decimals = decimals;
        break;
      case GridCellMoneyModel money:
        money.Decimals = decimals;
        break;
      case GridCellPercentModel pct:
        pct.Decimals = decimals;
        break;
      default:
        dto.Type = UiFieldType.Number;
        dto.Cell = new GridCellNumberModel { Decimals = decimals };
        break;
    }

    return this;
  }

  public GridColumn<TProp> Grouped(bool isGrouped = true)
  {
    if (dto.Cell is GridCellNumberModel num)
    {
      num.IsGrouped = isGrouped;
    }
    else
    {
      dto.Type = UiFieldType.Number;
      dto.Cell = new GridCellNumberModel { IsGrouped = isGrouped };
    }

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
      ReferenceService = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TSource)),
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
    dto.ReferenceService = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TGridService));
    if (dto.Path != null && !dto.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
      dto.FilterPath = dto.Path + "Id";
    }

    return this;
  }

  public GridColumn<TProp> AsReference<TGridService, TSource>(params Expression<Func<TSource, object?>>[] labelPaths)
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellReferenceModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.ReferenceService = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TGridService));
    if (dto.Path != null && !dto.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
      dto.FilterPath = dto.Path + "Id";
    }

    if (labelPaths is { Length: > 0 })
    {
      dto.ReferenceLabelPaths = labelPaths
        .Select(e => Columns.ToCamelPath(MemberPath.Of(e)))
        .Where(s => !string.IsNullOrEmpty(s))
        .ToList();
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
