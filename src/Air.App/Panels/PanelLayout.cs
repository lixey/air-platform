namespace Platform.App.Panels;

public static class PanelLayout
{
  public static PanelLayoutBuilder Row(string key) => new(key, PanelDirection.Row);
  public static PanelLayoutBuilder Column(string key) => new(key, PanelDirection.Column);
}
