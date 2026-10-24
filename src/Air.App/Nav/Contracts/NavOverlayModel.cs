namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavOverlayModel : NavButtonModel
{
  public OverlayDto Overlay { get; set; } = new();
}
