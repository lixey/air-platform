namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavMenuModel : NavButtonModel
{
  public List<NavElementModel> Elements { get; set; } = [];
}
