namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridHeaderCellDto
{
  public string Label { get; set; } = string.Empty;
  public int Colspan { get; set; } = 1;
  public HeaderModel Model { get; set; } = new GridHeaderLabelModel();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }
}
