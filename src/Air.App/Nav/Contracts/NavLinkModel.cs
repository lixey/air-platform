namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavLinkModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;
}
