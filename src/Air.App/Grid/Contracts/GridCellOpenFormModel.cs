namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridCellOpenFormModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? KeyPath { get; init; }
}
