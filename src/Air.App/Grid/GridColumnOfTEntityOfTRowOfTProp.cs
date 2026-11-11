using Platform.App.Internals;
using Platform.Data.Internals;

namespace Platform.App.Grid;

public sealed class GridColumn<TEntity, TRow, TProp>(GridColumnDto dto, Columns owner, bool hasExplicitLabel = false)
{
  private static readonly ConcurrentDictionary<(Type EntityType, string ExpressionKey), Delegate> CompiledColorSelectors = new();

  private bool _hasExplicitLabel = hasExplicitLabel;

  public GridColumnDto Dto => dto;

  public GridColumn<TEntity, TRow, TProp> Label(string label)
  {
    _hasExplicitLabel = true;
    dto.Label = label;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Title(string title)
  {
    dto.Title = title;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Title(string uiSource, string dbSource) =>
    Title(Feature.FormatSourceTitle(uiSource, dbSource));

  public GridColumn<TEntity, TRow, TProp> Format(Func<TProp, object?> formatter)
  {
    ArgumentNullException.ThrowIfNull(formatter);
    dto.ValueFormatter = val =>
    {
      if (val is TProp typed)
      {
        return formatter(typed);
      }

      if (val is null && default(TProp) is null)
      {
        return formatter(default!);
      }

      return val;
    };
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Width(int px)
  {
    dto.Width = px;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Width(string width)
  {
    dto.Width = int.TryParse(width.Replace("px", "").Trim(), out var px) ? px : 100;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> MaxWidth(int px)
  {
    dto.MaxWidth = px;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Sticky(bool sticky = true)
  {
    dto.IsSticky = sticky;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> StickyRight(bool stickyRight = true)
  {
    dto.IsStickyRight = stickyRight;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Wrap(bool wrap = true)
  {
    dto.IsWrapped = wrap;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Sortable(bool sortable = true)
  {
    dto.IsSortable = sortable;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Filterable(bool filterable = true)
  {
    dto.IsFilterable = filterable;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Computed()
  {
    dto.IsComputed = true;
    dto.IsSortable = false;
    dto.IsFilterable = false;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Hidden(bool hidden = true)
  {
    dto.IsHidden = hidden;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Disabled(bool disabled = true)
  {
    dto.IsDisabled = disabled;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Required(bool required = true)
  {
    dto.IsRequired = required;
    dto.ExplicitRequired = required;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Cell(CellModel cell)
  {
    dto.Cell = cell;
    ReapplyColorPathIfConfigured();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsOpenForm<TFormService>() where TFormService : class
  {
    dto.Cell = new GridCellOpenFormModel();
    owner.FormServiceType = typeof(TFormService);
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsOpenForm<TFormService>(Expression<Func<TEntity, object?>> keySelector) where TFormService : class
  {
    dto.Cell = new GridCellOpenFormModel
    {
      KeyPath = Columns.ToCamelPath(MemberPath.Of(keySelector))
    };
    owner.FormServiceType = typeof(TFormService);
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> OpenForm(Func<TRow, Type?> resolver)
  {
    dto.Cell = new GridCellOpenFormModel();
    dto.Width ??= 80;
    dto.IsSticky = true;
    dto.FormServiceResolver = row => row is TRow typed ? resolver(typed) : null;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsCover<TFileGrid>() where TFileGrid : class
  {
    dto.Path = (dto.Path ?? "id") + "Cover";
    dto.Label = "Cover";
    dto.IsSortable = false;
    dto.IsFilterable = false;
    dto.Cell = new GridCellCoverModel
    {
      Service = ServicePathResolver.Resolve(typeof(TFileGrid)),
      ParentService = owner.FormServiceType != null ? ServicePathResolver.Resolve(owner.FormServiceType) : string.Empty
    };
    owner.CoverFileGridType = typeof(TFileGrid);
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsImage()
  {
    dto.IsSortable = false;
    dto.IsFilterable = false;
    dto.Width ??= 56;
    dto.Cell = new GridCellCoverModel { Service = string.Empty, ParentService = string.Empty };
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsCheckbox()
  {
    dto.Type = UiFieldType.Checkbox;
    dto.Cell = new GridCellCheckboxModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsSelect(IEnumerable<OptionDto> options)
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
    ReapplyColorPathIfConfigured();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsDate()
  {
    dto.Type = UiFieldType.Date;
    dto.Cell = new GridCellDateModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsDatetime()
  {
    dto.Type = UiFieldType.Datetime;
    dto.Cell = new GridCellDatetimeModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsNumber()
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellNumberModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Decimals(int decimals)
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

  public GridColumn<TEntity, TRow, TProp> Grouped(bool isGrouped = true)
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

  public GridColumn<TEntity, TRow, TProp> AsMoney(string? currency = null)
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellMoneyModel { Currency = currency, Decimals = 2 };
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsPercent()
  {
    dto.Type = UiFieldType.Number;
    dto.Cell = new GridCellPercentModel { Decimals = 2 };
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Sum()
  {
    dto.IsSummed = true;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> SelectionSum(string unit = "", string? excludeColor = null)
  {
    dto.SelectionSumUnit = unit;
    dto.SelectionSumExcludeColor = excludeColor?.ToLowerInvariant();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Stepper(bool hasStepper = true)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.HasStepper = hasStepper;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Step(double step)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.Step = step;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Min(double min)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.Min = min;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Max(double max)
  {
    dto.NumberEdit ??= new GridNumberEditDto();
    dto.NumberEdit.Max = max;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Editable(bool isEditable = true)
  {
    dto.IsEditable = isEditable;
    if (isEditable && dto.NumberEdit == null && dto.Cell is GridCellNumberModel or GridCellMoneyModel or GridCellPercentModel)
    {
      dto.NumberEdit = new GridNumberEditDto { Step = 1, HasStepper = false };
    }

    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Editable(Func<bool> condition) =>
    Editable(condition());

  public GridColumn<TEntity, TRow, TProp> PickedBy<TSource>(Delegate method, int minLength = 2)
  {
    var readsParent = ParentScopeValidator.DeclaresParentScope(typeof(TSource));
    dto.PickerSourceType = typeof(TSource);
    dto.PickerTargetMethod = method.Method;
    dto.Cell = new GridCellPickerModel
    {
      ReferenceService = ServicePathResolver.Resolve(typeof(TSource)),
      Method = ServicePathResolver.ToKebabCase(method.Method.Name),
      MinLength = minLength,
      ReadsParent = readsParent
    };
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsReference<TGridService>() =>
    ConfigureReference<TGridService>(null);

  public GridColumn<TEntity, TRow, TProp> AsReference<TGridService>(
    Expression<Func<TProp, object?>> firstLabel,
    params Expression<Func<TProp, object?>>[] moreLabels) =>
    ConfigureReference<TGridService>(
      new[] { firstLabel }.Concat(moreLabels).Select(e => Columns.ToCamelPath(MemberPath.Of(e))));

  public GridColumn<TEntity, TRow, TSource> AsReference<TGridService, TSource>(
    params Expression<Func<TSource, object?>>[] labelPaths)
  {
    ConfigureReference<TGridService>(
      labelPaths is { Length: > 0 }
        ? labelPaths.Select(e => Columns.ToCamelPath(MemberPath.Of(e)))
        : null);
    return new GridColumn<TEntity, TRow, TSource>(dto, owner, _hasExplicitLabel);
  }

  public GridColumn<TEntity, TRow, TProp> Where<TVal>(Expression<Func<TProp, TVal>> sourceMember, TVal value) =>
    AddReferenceFilter(Columns.ToCamelPath(MemberPath.Of(sourceMember)), value);

  public GridColumn<TEntity, TRow, TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceMember, TVal value) =>
    AddReferenceFilter(Columns.ToCamelPath(MemberPath.Of(sourceMember)), value);

  public GridColumn<TEntity, TRow, TProp> FilterBy<TFilterVal>(Expression<Func<TEntity, TFilterVal>> filterExpression)
  {
    dto.FilterExpression = filterExpression;
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsPhone()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellPhoneModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsEmail()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellEmailModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> AsUrl()
  {
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellUrlModel();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> ColorByValue(Func<TProp, string?> colorSelector)
  {
    dto.ColorEvaluator = obj => obj is TProp val ? colorSelector(val) : null;
    ApplyColorPath();
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> Color(Expression<Func<TEntity, string?>> colorSelector)
  {
    ApplyColorSelector(colorSelector);
    return this;
  }

  public GridColumn<TEntity, TRow, TProp> ColorByRow(Func<TRow, string?> colorSelector)
  {
    dto.ColorEvaluator = obj => obj is TRow row ? colorSelector(row) : null;
    ApplyColorPath();
    return this;
  }

  private GridColumn<TEntity, TRow, TProp> ConfigureReference<TGridService>(IEnumerable<string>? rawLabelPaths)
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Cell = new GridCellReferenceModel();
    dto.Header = new GridHeaderReferenceModel();
    dto.ReferenceServiceType = typeof(TGridService);
    dto.ReferenceService = ServicePathResolver.Resolve(typeof(TGridService));
    if (dto.Path != null && !dto.Path.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
    {
      dto.FilterPath = dto.Path + "Id";
    }

    var resolvedLabels = rawLabelPaths?.Where(s => !string.IsNullOrEmpty(s)).ToList();
    if (resolvedLabels is { Count: > 0 })
    {
      dto.ReferenceLabelPaths = resolvedLabels;
      ReferenceLookupAllowlist.RegisterLabels(typeof(TGridService), resolvedLabels);
    }
    else
    {
      ReferenceLookupAllowlist.RegisterLabels(typeof(TGridService), ["name"]);
    }

    ReapplyColorPathIfConfigured();
    return this;
  }

  private GridColumn<TEntity, TRow, TProp> AddReferenceFilter<TVal>(string path, TVal value)
  {
    var rule = FilterNode.Rule(path, FilterOperator.Equals, value);
    dto.ReferenceFilter = CombineFilter(dto.ReferenceFilter, rule);
    if (!string.IsNullOrEmpty(dto.ReferenceService))
    {
      ReferenceLookupAllowlist.RegisterFilterField(
        dto.ReferenceServiceType ?? ModuleCatalog.Instance.FindServiceType(dto.ReferenceService),
        path);
    }

    return this;
  }

  private void StripDefaultIdLabel()
  {
    if (!_hasExplicitLabel && dto.Label is { } lbl && lbl.EndsWith(" Id", StringComparison.Ordinal) && lbl.Length > 3)
    {
      dto.Label = lbl[..^3];
    }
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

  private void ApplyColorSelector(Expression<Func<TEntity, string?>> colorSelector)
  {
    var navPath = ExtractNavigationPath(colorSelector.Body, out var leafMember);
    var exprKey = navPath != null && leafMember != null
      ? $"{navPath}.{leafMember}"
      : leafMember ?? colorSelector.ToString();
    var compiled = ExpressionClosureInspector.ContainsClosure(colorSelector.Body)
      ? colorSelector.Compile()
      : (Func<TEntity, string?>)CompiledColorSelectors.GetOrAdd(
          (typeof(TEntity), exprKey),
          _ => colorSelector.Compile());

    dto.ColorExpression = colorSelector;
    dto.ColorExpressionKey = exprKey;
    dto.ColorNavPath = navPath;
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
    ApplyColorPath();
  }

  private void ReapplyColorPathIfConfigured()
  {
    if (dto.ColorEvaluator != null || dto.ColorExpression != null || !string.IsNullOrEmpty(dto.ColorNavPath))
    {
      ApplyColorPath();
    }
  }

  private void ApplyColorPath()
  {
    var isRef = dto.Cell is GridCellReferenceModel or GridCellSelectModel ||
                dto.ReferenceService != null ||
                dto.Options is { Count: > 0 } ||
                typeof(Reference).IsAssignableFrom(dto.MemberType);
    dto.Cell.ColorPath = isRef ? $"{dto.Path}.color" : $"{dto.Path}Color";
  }

  internal static string? ExtractNavigationPath(Expression expr) =>
    ExtractNavigationPath(expr, out _);

  internal static string? ExtractNavigationPath(Expression expr, out string? leafMember)
  {
    var parts = new List<string>(4);
    var current = expr;
    while (current is MemberExpression me)
    {
      parts.Add(me.Member.Name);
      current = me.Expression;
    }

    if (current is not ParameterExpression || parts.Count == 0)
    {
      leafMember = null;
      return null;
    }

    parts.Reverse();
    leafMember = parts[^1];
    return parts.Count > 1 ? string.Join('.', parts.Take(parts.Count - 1)) : null;
  }
}
