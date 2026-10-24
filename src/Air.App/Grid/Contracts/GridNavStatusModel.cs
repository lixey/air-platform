namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavStatusModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;
  public string EmptyTitle { get; set; } = string.Empty;
  public List<GridNavStatusMoveDto> Moves { get; set; } = [];
}
