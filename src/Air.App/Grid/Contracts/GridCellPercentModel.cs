namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridCellPercentModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; set; }
}
