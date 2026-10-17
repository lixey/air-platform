using System.Linq.Expressions;
using Air.App.Components;
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
    var tabKey = key ?? label.ToLowerInvariant();
    _currentTab = tabKey;
    _tabs.Add(new FormTabDto { Key = tabKey, Label = label });
  }

  public void AddSeparator(string label)
  {
  }

  public ChildGridBuilder AddChild<TChildGridService>()
  {
    var child = new ChildGridBuilder(Air.App.Modules.ServicePathResolver.Resolve(typeof(TChildGridService)));
    _children.Add(child);
    return child;
  }

  public void UseRecordLabelOf<TGridService>() { }

  public void Disable(bool disabled = true) { }

  public FormElement<TProp> Add<TEntity, TProp>(Expression<Func<TEntity, TProp>> expression)
  {
    var path = ToCamelPath(MemberPath.Of(expression));
    var el = new FormElementDto
    {
      Path = path,
      Label = PathToLabel(path),
      Type = ResolveFieldType(typeof(TProp)),
      Model = ResolveDefaultModel(typeof(TProp)),
      Tab = _currentTab
    };
    _elements.Add(el);
    return new FormElement<TProp>(el);
  }

  public FormElement<TProp> Add<TProp>(string path)
  {
    var el = new FormElementDto
    {
      Path = path,
      Label = PathToLabel(path),
      Type = ResolveFieldType(typeof(TProp)),
      Model = ResolveDefaultModel(typeof(TProp)),
      Tab = _currentTab
    };
    _elements.Add(el);
    return new FormElement<TProp>(el);
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

  private static FormElementModel ResolveDefaultModel(Type type)
  {
    var underlying = Nullable.GetUnderlyingType(type) ?? type;
    if (underlying == typeof(bool)) return new FormCheckboxModel();
    if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) || underlying == typeof(DateOnly)) return new FormDateModel();
    if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal) || underlying == typeof(double)) return new FormNumberModel();
    return new FormInputModel();
  }
}

public class Elements<TEntity> : Elements
{
  public FormElement<TProp> Add<TProp>(Expression<Func<TEntity, TProp>> expression) =>
    Add<TEntity, TProp>(expression);
}

public sealed class FormElement<TProp>(FormElementDto dto)
{
  public FormElementDto Dto => dto;

  public FormElement<TProp> Label(string label) { dto.Label = label; return this; }
  public FormElement<TProp> Span(int cols) { dto.Span = cols; return this; }
  public FormElement<TProp> Required(bool required = true) { dto.IsRequired = required; return this; }
  public FormElement<TProp> Disabled(bool disabled = true) { dto.IsDisabled = disabled; return this; }
  public FormElement<TProp> Hidden(bool hidden = true) { dto.IsHidden = hidden; return this; }
  public FormElement<TProp> Placeholder(string placeholder) { dto.Placeholder = placeholder; return this; }
  public FormElement<TProp> Tab(string tab) { dto.Tab = tab; return this; }

  public FormElement<TProp> AsTextarea()
  {
    dto.Model = new FormTextareaModel();
    return this;
  }

  public FormElement<TProp> AsCheckbox()
  {
    dto.Type = UiFieldType.Checkbox;
    dto.Model = new FormCheckboxModel();
    return this;
  }

  public FormElement<TProp> AsDate()
  {
    dto.Type = UiFieldType.Date;
    dto.Model = new FormDateModel();
    return this;
  }

  public FormElement<TProp> AsDatetime()
  {
    dto.Type = UiFieldType.Datetime;
    dto.Model = new FormDatetimeModel();
    return this;
  }

  public FormElement<TProp> AsSelect(IEnumerable<OptionDto> options)
  {
    dto.Model = new FormSelectModel { Options = options.ToList() };
    return this;
  }

  public FormElement<TProp> AsPassword()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TProp> AsEmail()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormInputModel();
    return this;
  }

  public FormElement<TProp> AsTimeZone()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormInputModel();
    return this;
  }

  public IReadOnlySet<string> TreeKeys { get; private set; } = new HashSet<string>();

  public FormElement<TProp> AsRightsTree(Air.App.Modules.ModuleCatalog? catalog = null, IReadOnlySet<string>? held = null)
  {
    dto.Type = "rights-tree";
    catalog ??= Air.App.Modules.ModuleCatalog.Instance;
    var nodes = Air.App.Auth.RightsTree.Build(catalog, held);
    dto.Model = new FormCheckboxTreeModel { Nodes = nodes };
    TreeKeys = Air.App.Auth.RightsTree.FlattenKeys(nodes);
    return this;
  }

  public FormElement<TProp> AsInfo() => Disabled();
  public FormElement<TProp> AsPhone(string? mask = null) => this;
  public FormElement<TProp> InputOnly() => this;
  public FormElement<TProp> Unique(string? message = null) => this;
  public FormElement<TProp> AsCheckboxSet(IEnumerable<CheckboxOptionDto>? options = null)
  {
    dto.Type = "checkbox-set";
    dto.Model = new FormCheckboxSetModel { Options = options?.ToList() ?? [] };
    return this;
  }
  public FormElement<TProp> AsCheckboxSet(object? options)
  {
    if (options is IEnumerable<CheckboxOptionDto> opts) return AsCheckboxSet(opts);
    return this;
  }
  public FormElement<TProp> Where<TSource, TVal>(Expression<Func<TSource, TVal>> sourceProp, Expression<Func<TProp, TVal>> targetProp) => this;

  public FormElement<TProp> AsReference<TGridService>()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService))
    };
    return this;
  }

  public FormElement<TProp> AsReference<TGridService, TSource>()
  {
    dto.Type = UiFieldType.Text;
    dto.Model = new FormSelectModel
    {
      ReferenceService = Air.App.Modules.ServicePathResolver.Resolve(typeof(TGridService))
    };
    return this;
  }
}

public sealed class ChildGridBuilder(string servicePath)
{
  public string ServicePath => servicePath;
  public string DeclaredKey { get; private set; } = string.Empty;
  public string DeclaredLabel { get; private set; } = string.Empty;
  public bool IsFiles { get; private set; }

  public ChildGridBuilder Key(string key) { DeclaredKey = key; return this; }
  public ChildGridBuilder Label(string label) { DeclaredLabel = label; return this; }
  public ChildGridBuilder AsFiles(object? look = null) { IsFiles = true; return this; }
}
