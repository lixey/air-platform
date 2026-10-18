using System.Linq.Expressions;
using System.Reflection;
using Air.App.Components;
using Air.App.Localization;
using Air.Data;
using Air.Data.Contracts;
using Air.Data.Internal;

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

  public void AddTab(string label, string? key = null)
  {
    var tabKey = key ?? label.ToLowerInvariant().Replace(' ', '-');
    _currentTab = tabKey;
    _tabs.Add(new FormTabDto { Key = tabKey, Label = label });
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
    var child = new ChildGridBuilder(Air.App.Modules.ServicePathResolver.Resolve(typeof(TChildGridService)), typeof(TChildGridService));
    _children.Add(child);
    return child;
  }

  private bool _disabled;

  public void UseRecordLabelOf<TGridService>() { }

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
    for (int i = 0; i < parts.Length; i++)
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
    if (underlying == typeof(bool)) return UiFieldType.Checkbox;
    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)) return UiFieldType.Date;
    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal) || underlying == typeof(double)) return UiFieldType.Number;
    return UiFieldType.Text;
  }

  private static FormElementModel ResolveDefaultModel(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool)) return new FormCheckboxModel();
    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)) return new FormDateModel();
    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal) || underlying == typeof(double)) return new FormNumberModel();
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
  public FormElementDto Dto => dto;

  public FormElement<TEntity, TProp> Label(string label)
  {
    _hasExplicitLabel = true;
    dto.Label = label;
    return this;
  }
  public FormElement<TEntity, TProp> Span(int cols) { dto.Span = cols; return this; }
  public FormElement<TEntity, TProp> Required(bool required = true) { dto.IsRequired = required; return this; }
  public FormElement<TEntity, TProp> Disabled(bool disabled = true) { dto.IsDisabled = disabled; return this; }
  public FormElement<TEntity, TProp> Hidden(bool hidden = true) { dto.IsHidden = hidden; return this; }
  public FormElement<TEntity, TProp> Placeholder(string placeholder) { dto.Placeholder = placeholder; return this; }
  public FormElement<TEntity, TProp> Tab(string tab) { dto.Tab = tab; return this; }

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
        Color = o.Color
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
      Inherit = Localizer.Instance["TimeZoneAsOrganisation"]
    };
    return this;
  }

  public IReadOnlySet<string> TreeKeys { get; private set; } = new HashSet<string>();

  public FormElement<TEntity, TProp> AsRightsTree(Air.App.Modules.ModuleCatalog? catalog = null, IReadOnlySet<string>? held = null)
  {
    dto.Type = "rights-tree";
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

  public FormElement<TEntity, TProp> AsPhone(string? mask = null)
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormPhoneModel
    {
      Masks = mask != null ? [mask] : ["+380 (##) ###-##-##", "+1 (###) ###-####"]
    };
    return this;
  }

  public FormElement<TEntity, TProp> InputOnly()
  {
    dto.IsInputOnly = true;
    return this;
  }

  public FormElement<TEntity, TProp> Unique(string? message = null) => this;
  public FormElement<TEntity, TProp> Unique(string? message, params Expression<Func<TEntity, object?>>[] withKeys) => this;
  public FormElement<TEntity, TProp> AsCheckboxSet(IEnumerable<CheckboxOptionDto>? options = null)
  {
    dto.Type = "checkbox-set";
    dto.Model = new FormCheckboxSetModel { Options = options?.ToList() ?? [] };
    return this;
  }
  public FormElement<TEntity, TProp> AsCheckboxSet(object? options)
  {
    if (options is IEnumerable<CheckboxOptionDto> opts) return AsCheckboxSet(opts);
    return this;
  }

  public FormElement<TEntity, TProp> ServerOwned(bool serverOwned = true)
  {
    dto.IsServerOwned = serverOwned;
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
    if (expr is UnaryExpression ue) expr = ue.Operand;
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
    dto.ReferencePath ??= dto.Path.EndsWith("Id", StringComparison.Ordinal) ? dto.Path[..^2] : dto.Path;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService)),
      ReferenceLabelPaths = ["name"]
    };
    return new FormReferenceElement<TEntity, TProp, TSource>(dto);
  }
}

public class FormReferenceElement<TEntity, TProp, TSource>(FormElementDto dto) : FormElement<TEntity, TProp>(dto)
{
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
    if (existing is null) return addition;
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
}

public sealed class ChildGridBuilder(string servicePath, Type? serviceType = null)
{
  public string ServicePath => servicePath;
  public Type? ServiceType => serviceType;
  public string DeclaredKey { get; private set; } = string.Empty;
  public string DeclaredLabel { get; private set; } = string.Empty;
  public bool IsFiles { get; private set; }
  public string Look { get; private set; } = Contracts.FilesLook.Tiles;

  public ChildGridBuilder Key(string key) { DeclaredKey = key; return this; }
  public ChildGridBuilder Label(string label) { DeclaredLabel = label; return this; }
  public ChildGridBuilder AsFiles(string look = Contracts.FilesLook.Tiles) { IsFiles = true; Look = look; return this; }
}
