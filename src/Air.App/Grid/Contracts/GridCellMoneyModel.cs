namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridCellMoneyModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; set; }
}
