namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportPieSliceDto
{
  public string Name { get; init; } = string.Empty;
  public decimal Value { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }
}
