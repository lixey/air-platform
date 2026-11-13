namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class ColumnSummaryDto
{
  [OneOf(typeof(SummaryKind))]
  public string Kind { get; set; } = SummaryKind.Sum;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Unit { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? ExcludeColor { get; set; }
}
