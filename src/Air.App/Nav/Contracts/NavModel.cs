namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavModel : PanelModel
{
  public string Key { get; set; } = string.Empty;
  public List<NavElementModel> Start { get; set; } = [];
  public List<NavElementModel> Elements { get; set; } = [];
  public List<NavElementModel> End { get; set; } = [];
  public List<NavElementModel> More { get; set; } = [];
  public NavOptionsDto Options { get; set; } = new();
}
