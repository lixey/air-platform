namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridCellNumberModel : CellModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? IsGrouped { get; set; }
}
