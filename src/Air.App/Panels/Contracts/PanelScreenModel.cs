namespace Platform.App.Panels.Contracts;

[UiContract]
public sealed record PanelScreenModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
}
