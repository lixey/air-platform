namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavStatusMoveDto
{
  public List<long> From { get; set; } = [];
  public GridNavActionModel Action { get; set; } = new();
}
