using Platform.App.Internals;

namespace Platform.App.Grid;

public sealed class GridNav(Localizer? localizer = null)
{
  private readonly Localizer _l = localizer ?? Localizer.Instance;
  internal List<GridNavPickerModel> Pickers { get; } = [];
  internal List<(Type SourceType, MethodInfo Method, GridNavPickerModel Model)> PickerEntries { get; } = [];
  internal List<(Type FormType, GridNavAddModel Model)> Creates { get; } = [];
  internal List<Action<NavModel, NavModel>> Customizers { get; } = [];

  public List<NavElementModel> Start { get; } = [];
  public List<NavElementModel> Elements { get; } = [];
  public List<NavElementModel> End { get; } = [];
  public List<NavElementModel> More { get; } = [];
  public List<NavElementModel> Selection { get; } = [];

  public bool ShowCreate { get; set; } = true;
  public bool ShowSearch { get; set; } = true;
  public bool ShowFilters { get; set; } = true;
  public bool ShowExport { get; set; } = true;
  public bool ShowDelete { get; set; } = true;

  public GridNav AddStart(NavElementModel model)
  {
    Start.Add(model);
    return this;
  }

  public GridNav Add(NavElementModel model)
  {
    Elements.Add(model);
    return this;
  }

  public GridNav AddEnd(NavElementModel model)
  {
    End.Add(model);
    return this;
  }

  public GridNav AddMore(NavElementModel model)
  {
    More.Add(model);
    return this;
  }

  public GridNav AddSelection(NavElementModel model)
  {
    Selection.Add(model);
    return this;
  }

  public GridNav WithoutCreate()
  {
    ShowCreate = false;
    return this;
  }

  public GridNav WithoutSearch()
  {
    ShowSearch = false;
    return this;
  }

  public GridNav WithoutFilters()
  {
    ShowFilters = false;
    return this;
  }

  public GridNav WithoutExport()
  {
    ShowExport = false;
    return this;
  }

  public GridNav WithoutDelete()
  {
    ShowDelete = false;
    return this;
  }

  public GridNav Customize(Action<NavModel> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    Customizers.Add((nav, _) => configure(nav));
    return this;
  }

  public GridNav Customize(Action<NavModel, NavModel> configure)
  {
    ArgumentNullException.ThrowIfNull(configure);
    Customizers.Add(configure);
    return this;
  }

  public void AddCreate<TFormService>(Action<GridButtonBuilder>? look = null) where TFormService : class
  {
    var b = new GridButtonBuilder();
    look?.Invoke(b);
    var targetService = ServicePathResolver.Resolve(typeof(TFormService));
    var rawLabel = b.LabelText ?? (b.TitleText != null ? null : "Add");
    Creates.Add((typeof(TFormService), new GridNavAddModel
    {
      Key = $"add.{targetService.Replace('/', '.')}",
      Service = targetService,
      Label = !string.IsNullOrEmpty(rawLabel) ? _l[rawLabel] : null,
      Title = b.TitleText != null ? _l[b.TitleText] : null,
      Icon = b.IconHandle ?? UiIcon.Plus,
      Color = b.ColorHandle ?? UiColor.Orange,
      Variant = b.VariantHandle ?? ButtonVariant.Soft
    }));
  }

  public GridNavPickerBuilder AddPicker<TSource>(
    Delegate method,
    string? label = null)
  {
    var readsParent = ParentScopeValidator.DeclaresParentScope(typeof(TSource));
    var picker = new GridNavPickerModel
    {
      Key = method.Method.Name.ToLowerInvariant(),
      Method = method.Method.Name,
      Label = label != null ? _l[label] : null,
      ReferenceService = ServicePathResolver.Resolve(typeof(TSource)),
      MinLength = 2,
      ReadsParent = readsParent
    };
    Pickers.Add(picker);
    PickerEntries.Add((typeof(TSource), method.Method, picker));
    return new GridNavPickerBuilder(picker);
  }
}
