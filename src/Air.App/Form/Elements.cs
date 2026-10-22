namespace Air.App.Form;

public class Elements
{
  private readonly List<FormElementDto> _elements = [];
  private readonly List<FormTabDto> _tabs = [];
  private string? _currentTab;

  private readonly List<ChildGridBuilder> _children = [];
  public IReadOnlyList<FormElementDto> List => _elements;
  public IReadOnlyList<FormTabDto> Tabs => _tabs;
  public IReadOnlyList<ChildGridBuilder> Children => _children;

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
    var child = new ChildGridBuilder(Air.App.Modules.ServicePathResolver.Resolve(typeof(TChildGridService)), typeof(TChildGridService), _currentTab);
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

  public void UseStatusOf<TEntity>(Screens.IRecordStatuses<TEntity> statuses) where TEntity : class
  {
    StatusFlow = statuses;
  }

  public FormElement<TEntity, TProp> Add<TEntity, TProp>(Expression<Func<TEntity, TProp>> expression)
  {
    var path = ToCamelPath(MemberPath.Of(expression));
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
    _elements.Add(el);
    return new FormElement<TEntity, TProp>(el);
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

public class Elements<TEntity> : Elements where TEntity : class
{
  public FormElement<TEntity, TProp> Add<TProp>(Expression<Func<TEntity, TProp>> expression) =>
    Add<TEntity, TProp>(expression);

  public void UseStatusOf(Screens.IRecordStatuses<TEntity> statuses) =>
    base.UseStatusOf(statuses);
}

public class FormElement<TProp>(FormElementDto dto) : FormElement<object, TProp>(dto)
{
}

public class FormElement<TEntity, TProp>(FormElementDto dto)
{
  private bool _hasExplicitLabel;
  private bool _hasSpan;
  public FormElementDto Dto => dto;

  public FormElement<TEntity, TProp> Label(string label)
  {
    _hasExplicitLabel = true;
    dto.Label = label;
    return this;
  }
  public FormElement<TEntity, TProp> Title(string title)
  {
    dto.Title = title;
    return this;
  }
  public FormElement<TEntity, TProp> Title(string legacyUi, string legacyDb)
  {
    dto.Title = $"Легасі UI: {legacyUi} | БД: {legacyDb}";
    return this;
  }
  public FormElement<TEntity, TProp> Span(int cols)
  {
    _hasSpan = true;
    dto.Span = cols;
    return this;
  }
  public FormElement<TEntity, TProp> Required(bool required = true)
  {
    dto.ExplicitRequired = required;
    dto.IsRequired = required;
    return this;
  }
  public FormElement<TEntity, TProp> Disabled(bool disabled = true)
  {
    dto.IsDisabled = disabled;
    return this;
  }
  public FormElement<TEntity, TProp> Hidden(bool hidden = true)
  {
    dto.IsHidden = hidden;
    return this;
  }
  public FormElement<TEntity, TProp> Focus(bool focus = true)
  {
    dto.IsFocused = focus;
    return this;
  }
  public FormElement<TEntity, TProp> Placeholder(string placeholder)
  {
    dto.Placeholder = placeholder;
    return this;
  }
  public FormElement<TEntity, TProp> Tab(string tab)
  {
    dto.Tab = tab;
    return this;
  }

  public FormElement<TEntity, TProp> Range(double min, double max)
  {
    dto.Constraints.Min = System.Text.Json.JsonSerializer.SerializeToElement(min);
    dto.Constraints.Max = System.Text.Json.JsonSerializer.SerializeToElement(max);
    return this;
  }

  public FormElement<TEntity, TProp> Min(decimal min)
  {
    dto.Constraints.Min = System.Text.Json.JsonSerializer.SerializeToElement(min);
    return this;
  }

  public FormElement<TEntity, TProp> Max(decimal max)
  {
    dto.Constraints.Max = System.Text.Json.JsonSerializer.SerializeToElement(max);
    return this;
  }

  public FormElement<TEntity, TProp> AsPercent()
  {
    dto.Model = new FormPercentModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsMoney(string? currency = null)
  {
    dto.Type = UiFieldType.Number;
    dto.Model = new FormMoneyModel { Currency = currency };
    return this;
  }

  public FormElement<TEntity, TProp> AsTextarea()
  {
    dto.Model = new FormTextareaModel();
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    return this;
  }

  public FormElement<TEntity, TProp> AsCheckbox()
  {
    dto.Type = UiFieldType.Checkbox;
    dto.Model = new FormCheckboxModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsDate()
  {
    dto.Type = UiFieldType.Date;
    dto.Model = new FormDateModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsDatetime()
  {
    dto.Type = UiFieldType.Datetime;
    dto.Model = new FormDatetimeModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsSelect(IEnumerable<OptionDto> options)
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.Model = new FormSelectModel
    {
      Options = options.Select(o => new OptionDto
      {
        Id = o.Id,
        Name = Localizer.Instance[o.Name],
        Color = o.Color,
        Title = o.Title != null ? Localizer.Instance[o.Title] : null
      }).ToList()
    };
    return this;
  }

  public FormElement<TEntity, TProp> AsPassword()
  {
    dto.Type = FormFieldType.Password;
    dto.IsSecret = true;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsEmail()
  {
    dto.Type = FormFieldType.Email;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsTimeZone()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormTimeZoneModel
    {
      Zones = [.. TimeZones.Offered],
      Inherit = Localizer.Instance["TimeZoneByBrowser"]
    };
    return this;
  }

  public IReadOnlySet<string> TreeKeys { get; private set; } = new HashSet<string>();

  public FormElement<TEntity, TProp> AsRightsTree(Air.App.Modules.ModuleCatalog? catalog = null, IReadOnlySet<string>? held = null)
  {
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    dto.IsRightsTree = held == null;
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    catalog ??= Air.App.Modules.ModuleCatalog.Instance;
    var nodes = Air.App.Auth.RightsTree.Build(catalog, held);
    dto.Model = new FormCheckboxTreeModel { Nodes = nodes };
    TreeKeys = Air.App.Auth.RightsTree.FlattenKeys(nodes);
    return this;
  }

  public FormElement<TEntity, TProp> AsInfo()
  {
    dto.IsDisabled = true;
    dto.IsServerOwned = true;
    dto.Model = new FormInfoModel();
    return this;
  }

  public FormElement<TEntity, TProp> Help(string help)
  {
    dto.Help = help;
    return this;
  }

  public FormElement<TEntity, TProp> Grouped(bool isGrouped = true)
  {
    if (dto.Model is FormNumberModel num)
    {
      num.IsGrouped = isGrouped;
    }
    else if (dto.Model is FormInfoModel info)
    {
      info.IsGrouped = isGrouped;
    }

    return this;
  }

  public FormElement<TEntity, TProp> AsUrl()
  {
    dto.Type = FormFieldType.Url;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TEntity, TProp> AsPhone(params string[] masks)
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormPhoneModel
    {
      Masks = masks.Length > 0 ? [.. masks] : ["+380 (##) ###-##-##", "+1 (###) ###-####"]
    };
    return this;
  }

  public FormElement<TEntity, TProp> InputOnly()
  {
    dto.IsInputOnly = true;
    return this;
  }

  public FormElement<TEntity, TProp> Unique(string? message = null)
  {
    dto.UniqueMessage = message ?? "FormValidationUnique";
    dto.UniqueWithin = [];
    return this;
  }

  public FormElement<TEntity, TProp> Unique(string? message, params Expression<Func<TEntity, object?>>[] withKeys)
  {
    dto.UniqueMessage = message ?? "FormValidationUnique";
    dto.UniqueWithin = withKeys.Select(k => GetMemberName(k.Body)).Where(n => n != null).Select(n => n!).ToList();
    return this;
  }

  public FormElement<TEntity, TProp> AsCheckboxSet(IEnumerable<CheckboxOptionDto>? options = null)
  {
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    dto.Model = new FormCheckboxSetModel { Options = options?.ToList() ?? [] };
    return this;
  }
  public FormElement<TEntity, TProp> AsCheckboxSet(object? options)
  {
    if (options is IEnumerable<CheckboxOptionDto> opts)
    {
      return AsCheckboxSet(opts);
    }

    return this;
  }

  public FormElement<TEntity, TProp> ServerOwned(bool serverOwned = true)
  {
    dto.IsServerOwned = serverOwned;
    dto.IsDisabled |= serverOwned;
    return this;
  }

  public FormElement<TEntity, TProp> Stepper(bool hasStepper = true)
  {
    if (dto.Model is FormNumberModel numModel)
    {
      numModel.HasStepper = hasStepper;
    }

    return this;
  }

  public FormElement<TEntity, TProp> Step(double step)
  {
    if (dto.Model is FormNumberModel numModel)
    {
      numModel.Step = step;
    }

    return this;
  }

  internal static string? GetMemberName(Expression expr)
  {
    if (expr is UnaryExpression ue)
    {
      expr = ue.Operand;
    }

    return expr is MemberExpression me ? me.Member.Name : null;
  }

  internal static string ToCamel(string str) =>
    string.IsNullOrEmpty(str) ? str : char.ToLowerInvariant(str[0]) + str[1..];

  private void StripDefaultIdLabel()
  {
    if (!_hasExplicitLabel && dto.Label is { } lbl && lbl.EndsWith(" Id", StringComparison.Ordinal) && lbl.Length > 3)
    {
      dto.Label = lbl[..^3];
    }
  }

  public FormReferenceElement<TEntity, TProp, object> AsReference<TGridService>()
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.Reference;
    dto.ReferenceServiceType = typeof(TGridService);
    if (dto.ExplicitRequired == null && typeof(TProp).IsValueType && Nullable.GetUnderlyingType(typeof(TProp)) == null && !dto.IsServerOwned)
    {
      dto.IsRequired = true;
    }

    dto.ReferencePath ??= dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"]
    };
    return new FormReferenceElement<TEntity, TProp, object>(dto);
  }

  public FormReferenceElement<TEntity, TProp, TSource> AsReference<TGridService, TSource>()
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.Reference;
    dto.ReferenceServiceType = typeof(TGridService);
    if (dto.ExplicitRequired == null && typeof(TProp).IsValueType && Nullable.GetUnderlyingType(typeof(TProp)) == null && !dto.IsServerOwned)
    {
      dto.IsRequired = true;
    }

    dto.ReferencePath ??= dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"]
    };
    return new FormReferenceElement<TEntity, TProp, TSource>(dto);
  }

  public FormReferenceElement<TEntity, TProp, object> AsReferences<TGridService>() =>
    AsReferences<TGridService, object>();

  public FormReferenceElement<TEntity, TProp, TSource> AsReferences<TGridService, TSource>()
  {
    StripDefaultIdLabel();
    if (!_hasExplicitLabel && dto.Label is { } lbl && lbl.EndsWith(" Ids", StringComparison.Ordinal) && lbl.Length > 4)
    {
      dto.Label = lbl[..^4] + "s";
    }

    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    dto.ReferenceServiceType = typeof(TGridService);
    dto.ReferencePath ??= dto.Path.EndsWith("Ids", StringComparison.Ordinal)
      ? dto.Path[..^3] + "s"
      : (dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path + "Refs");
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"],
      IsMultiple = true
    };
    return new FormReferenceElement<TEntity, TProp, TSource>(dto);
  }
}

public class FormReferenceElement<TEntity, TProp, TSource>(FormElementDto dto) : FormElement<TEntity, TProp>(dto)
{
  public FormReferenceElement<TEntity, TProp, TSource> PatchOnSelect(
    Expression<Func<TEntity, object?>> target,
    Expression<Func<TSource, object?>> source) =>
    PatchOnSelect<TSource>(target, source);

  public FormReferenceElement<TEntity, TProp, TSource> PatchOnSelect<TSourceRow>(
    Expression<Func<TEntity, object?>> target,
    Expression<Func<TSourceRow, object?>> source)
  {
    var targetPath = GetMemberName(target.Body);
    var sourcePath = GetMemberName(source.Body);
    if (targetPath != null && sourcePath != null)
    {
      dto.SelectPatches ??= [];
      dto.SelectPatches.Add(new SelectPatchDto
      {
        TargetField = ToCamel(targetPath),
        SourceColumn = ToCamel(sourcePath)
      });
    }

    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> Where<TSourceVal, TDraftVal>(
    Expression<Func<TSource, TSourceVal>> sourceMember,
    Expression<Func<TEntity, TDraftVal>> draftMember)
  {
    if (dto.Model is FormSelectModel selectModel)
    {
      var sourcePath = Elements.ToCamelPath(MemberPath.Of(sourceMember));
      var draftPath = Elements.ToCamelPath(MemberPath.Of(draftMember));
      selectModel.ReferenceDraftRules ??= [];
      selectModel.ReferenceDraftRules.Add(new ReferenceDraftRuleDto
      {
        SourceField = sourcePath,
        DraftField = draftPath
      });
    }

    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> Where<TVal>(
    Expression<Func<TSource, TVal>> sourceMember,
    TVal value)
  {
    if (dto.Model is FormSelectModel selectModel)
    {
      var sourcePath = Elements.ToCamelPath(MemberPath.Of(sourceMember));
      var rule = FilterNode.Rule(sourcePath, FilterOperator.Equals, value);
      selectModel.ReferenceFilter = CombineFilter(selectModel.ReferenceFilter, rule);
    }

    return this;
  }

  private static FilterNode CombineFilter(FilterNode? existing, FilterNode addition)
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

  public new FormReferenceElement<TEntity, TProp, TSource> Required(bool required = true)
  {
    base.Required(required);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Disabled(bool disabled = true)
  {
    base.Disabled(disabled);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Span(int cols)
  {
    base.Span(cols);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Label(string label)
  {
    base.Label(label);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Title(string title)
  {
    base.Title(title);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Title(string legacyUi, string legacyDb)
  {
    base.Title(legacyUi, legacyDb);
    return this;
  }

  public new FormReferenceElement<TEntity, TProp, TSource> Focus(bool focus = true)
  {
    base.Focus(focus);
    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> AllowCreate(bool allow = true)
  {
    Dto.AllowsCreate = allow;
    return this;
  }

  public FormReferenceElement<TEntity, TProp, TSource> AllowCreate<TFormService>() where TFormService : class
  {
    Dto.AllowsCreate = true;
    Dto.CreateFormServiceType = typeof(TFormService);
    return this;
  }
}

public sealed class ChildGridBuilder(string servicePath, Type? serviceType = null, string? tab = null)
{
  public string ServicePath { get; } = servicePath;
  public Type? ServiceType => serviceType;
  public string? Tab => tab;
  public string DeclaredKey { get; private set; } = servicePath;
  public string DeclaredLabel { get; private set; } = string.Empty;
  public bool IsFiles { get; private set; }
  public string Look { get; private set; } = Contracts.FilesLook.Tiles;

  public ChildGridBuilder Key(string key)
  {
    DeclaredKey = key;
    return this;
  }
  public ChildGridBuilder Label(string label)
  {
    DeclaredLabel = label;
    return this;
  }
  public ChildGridBuilder AsFiles(string look = Contracts.FilesLook.Tiles)
  {
    IsFiles = true;
    Look = look;
    return this;
  }
}
