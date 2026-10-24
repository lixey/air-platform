namespace Platform.App.Form;

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

  public FormElement<TEntity, TProp> AsRightsTree(Platform.Api.Modules.ModuleCatalog? catalog = null, IReadOnlySet<string>? held = null)
  {
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.List;
    dto.IsRightsTree = held == null;
    if (!_hasSpan)
    {
      dto.Span = 12;
    }

    catalog ??= Platform.Api.Modules.ModuleCatalog.Instance;
    var nodes = Platform.App.Auth.RightsTree.Build(catalog, held);
    dto.Model = new FormCheckboxTreeModel { Nodes = nodes };
    TreeKeys = Platform.App.Auth.RightsTree.FlattenKeys(nodes);
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

  public FormElement<TEntity, TProp> AsCheckboxSet(IEnumerable<OptionDto>? options = null)
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
    if (options is IEnumerable<OptionDto> opts)
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

  public FormElement<TEntity, TProp> SaveOnChange(bool save = true)
  {
    dto.SavesOnChange = save;
    return this;
  }

  public FormElement<TEntity, TProp> Rules(Action<Actions.ActionInputRuleBuilder<TProp>> configure)
  {
    var builder = new Actions.ActionInputRuleBuilder<TProp>(dto);
    configure(builder);
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
      ReferenceService = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"]
    };
    return new FormReferenceElement<TEntity, TProp, object>(dto);
  }

  public FormReferenceElement<TEntity, TProp, TSource> AsReference<TGridService, TSource>(
    params Expression<Func<TSource, object?>>[] label)
  {
    StripDefaultIdLabel();
    dto.Type = UiFieldType.Text;
    dto.ValueKind = FormValueKind.Reference;
    dto.ReferenceServiceType = typeof(TGridService);
    if (dto.ExplicitRequired == null && typeof(TProp).IsValueType && Nullable.GetUnderlyingType(typeof(TProp)) == null && !dto.IsServerOwned)
    {
      dto.IsRequired = true;
    }

    var labelPaths = label
      .Select(expr => GetMemberName(expr.Body))
      .Where(n => !string.IsNullOrEmpty(n))
      .Select(n => ToCamel(n!))
      .ToList();
    if (labelPaths.Count == 0)
    {
      labelPaths = ["name"];
    }

    dto.ReferencePath ??= dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = labelPaths
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
      ReferenceService = Platform.Api.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"],
      IsMultiple = true
    };
    return new FormReferenceElement<TEntity, TProp, TSource>(dto);
  }
}
