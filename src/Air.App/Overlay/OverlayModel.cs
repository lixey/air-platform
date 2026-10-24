namespace Platform.App.Overlay;

public static class OverlayModel
{
  public static OverlayBuilder Dialog(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Dialog, layout);
  public static OverlayBuilder Drawer(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Drawer, layout);
  public static OverlayBuilder Popover(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Popover, layout);
  public static OverlayBuilder Menu(string key, PanelLayoutDto? layout = null) => new(key, OverlayPresentation.Menu, layout);
}
