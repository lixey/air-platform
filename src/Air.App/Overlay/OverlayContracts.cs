using Air.App.Components;
using Air.App.Panels;

namespace Air.App.Overlay;

public static class OverlayPresentation
{
  public const string Dialog = "dialog";
  public const string Drawer = "drawer";
  public const string Popover = "popover";
  public const string Menu = "menu";
}

[UiContract]
public sealed class OverlayScreenDto
{
  public string Service { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public object? Params { get; set; }
}

[UiContract]
public sealed class OverlayModelDto
{
  public string Key { get; set; } = string.Empty;

  [OneOf(typeof(OverlayPresentation))]
  public string Presentation { get; set; } = OverlayPresentation.Dialog;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public PanelLayoutDto? Layout { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayScreenDto? Screen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Width { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Height { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? MaxWidth { get; set; }

  [ClientOptional]
  public bool ClosesOnEscape { get; set; } = true;

  [ClientOptional]
  public bool ClosesOnOutside { get; set; } = true;
}

public static class OverlayModel
{
  public static OverlayBuilder Dialog(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Dialog, layout);
  public static OverlayBuilder Drawer(string key, string? service = null) =>
    new OverlayBuilder(key, OverlayPresentation.Drawer).Screen(service ?? key);
  public static OverlayBuilder Popover(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Popover, layout);
  public static OverlayBuilder Menu(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Menu, layout);
}

public sealed class OverlayBuilder(string key, string presentation, PanelLayoutDto? layout = null)
{
  private readonly OverlayModelDto _dto = new() { Key = key, Presentation = presentation, Layout = layout };

  public OverlayBuilder Screen(string servicePath, object? paramsObj = null)
  {
    _dto.Screen = new OverlayScreenDto { Service = servicePath, Params = paramsObj };
    return this;
  }

  public OverlayBuilder Width(string width) { _dto.Width = width; return this; }
  public OverlayBuilder Width(int px) => Width(PanelSize.Px(px));
  public OverlayBuilder Height(string height) { _dto.Height = height; return this; }
  public OverlayBuilder Height(int px) => Height(PanelSize.Px(px));
  public OverlayBuilder MaxWidth(int px) { _dto.MaxWidth = PanelSize.Px(px); return this; }

  public OverlayModelDto Build() => _dto;
}

[UiContract]
public sealed record OverlayConfirmModel : PanelModel
{
  public string Message { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? ConfirmLabel { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? CancelLabel { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? TypedAnswer { get; set; }
}

[UiContract]
public sealed record OverlayDirtyModel : PanelModel
{
  public string Title { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string SaveLabel { get; set; } = string.Empty;
  public string DiscardLabel { get; set; } = string.Empty;
  public string ContinueLabel { get; set; } = string.Empty;
}
