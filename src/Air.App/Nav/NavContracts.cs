using Air.App.Components;
using Air.App.Overlay;

namespace Air.App.Nav;

[UiContract]
public static class NavDirection
{
  public const string Row = "row";
  public const string Column = "column";
}

[UiContract]
public static class NavOverflow
{
  public const string Scroll = "scroll";
  public const string Wrap = "wrap";
}

[UiContract]
public sealed class NavOptionsDto
{
  [OneOf(typeof(NavDirection))]
  public string Direction { get; set; } = NavDirection.Row;

  [OneOf(typeof(NavOverflow))]
  public string Overflow { get; set; } = NavOverflow.Scroll;
}

[UiContract]
public sealed record NavModel : PanelModel
{
  public string Key { get; set; } = string.Empty;
  public List<NavElementModel> Start { get; set; } = [];
  public List<NavElementModel> Elements { get; set; } = [];
  public List<NavElementModel> End { get; set; } = [];
  public List<NavElementModel> More { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public NavOptionsDto? Options { get; set; }
}

[ClientOptional]
[UiContract]
public abstract record NavButtonModel : NavElementModel
{
  public string? Label { get; set; }
  public string? Title { get; set; }
  public string? Icon { get; set; }

  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }

  [OneOf(typeof(ButtonVariant))]
  public string? Variant { get; set; }

  [OneOf(typeof(ButtonSize))]
  public string? Size { get; set; }

  public string? Badge { get; set; }
  public bool IsDisabled { get; set; }
  public bool IsSelected { get; set; }
}

[UiContract]
public sealed record NavLinkModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;
}

[UiContract]
public sealed record NavTabModel : NavButtonModel
{
  public string Group { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }
}

[UiContract]
public sealed record NavTabsModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsPagedOnly { get; set; }
}

[UiContract]
public sealed record NavCloseModel : NavButtonModel;

[UiContract]
public sealed record NavLabelModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }
}

[UiContract]
public sealed record NavMenuModel : NavButtonModel
{
  public List<NavElementModel> Elements { get; set; } = [];
}

[UiContract]
public sealed record NavOverlayModel : NavButtonModel
{
  public OverlayModelDto Overlay { get; set; } = new();
}

[UiContract]
public sealed record NavSearchModel : NavElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  public string Value { get; set; } = string.Empty;
}

[UiContract]
public sealed record NavSeparatorModel : NavElementModel;

[UiContract]
public sealed record NavToggleModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool TargetsRoot { get; set; }
}

// Form Navigation Buttons
[UiContract]
public sealed record FormNavActionModel : NavButtonModel
{
  public string Service { get; set; } = string.Empty;
  public string Method { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayConfirmModel? Confirm { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public FormAskModel? Ask { get; set; }
}

[UiContract]
public sealed record FormNavSaveModel : NavButtonModel;

[UiContract]
public sealed record FormNavSaveCloseModel : NavButtonModel;

[UiContract]
public sealed record FormNavSaveOpenModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }
}
