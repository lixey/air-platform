namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed class NavOptionsDto
{
  [OneOf(typeof(NavDirection))]
  public string Direction { get; set; } = NavDirection.Row;

  [OneOf(typeof(NavOverflow))]
  public string Overflow { get; set; } = NavOverflow.Scroll;
}
