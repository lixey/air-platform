namespace Platform.App.Overlay.Contracts;

[UiContract]
public sealed record OverlayDirtyModel : PanelModel
{
  public string Title { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public string SaveLabel { get; set; } = string.Empty;
  public string DiscardLabel { get; set; } = string.Empty;
  public string ContinueLabel { get; set; } = string.Empty;
}
