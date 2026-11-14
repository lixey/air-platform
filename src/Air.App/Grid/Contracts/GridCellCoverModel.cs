namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridCellCoverModel : CellModel
{
  public required string Service { get; set; }
  public required string ParentService { get; set; }
}
