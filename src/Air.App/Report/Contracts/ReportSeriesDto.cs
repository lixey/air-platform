namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportSeriesDto
{
  public string Name { get; init; } = string.Empty;

  [OneOf(typeof(ReportChartKind))]
  public string Kind { get; init; } = ReportChartKind.Bar;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Stack { get; init; }

  public List<decimal> Data { get; init; } = [];
}
