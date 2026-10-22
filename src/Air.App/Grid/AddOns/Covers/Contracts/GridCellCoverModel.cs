namespace Air.App.Grid.AddOns.Covers.Contracts;

[UiContract]
public sealed record GridCellCoverModel : CellModel
{
  public required string Service { get; set; }
  public required string ParentService { get; set; }
}
