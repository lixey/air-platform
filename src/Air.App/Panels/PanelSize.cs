namespace Platform.App.Panels;

public static class PanelSize
{
  public const string Fill = "1fr";
  public const string Auto = "auto";

  public static string Px(int value) => $"{value}px";
  public static string Percent(int value) => $"{value}%";
  public static string Fr(int value) => $"{value}fr";
}
