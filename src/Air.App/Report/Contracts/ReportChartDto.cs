namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportChartDto
{
  public string Key { get; init; } = string.Empty;
  public string Title { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; init; }

  [OneOf(typeof(ReportChartKind))]
  public string Kind { get; init; } = ReportChartKind.Bar;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Height { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Span { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<string>? Categories { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportSeriesDto>? Series { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportPieSliceDto>? Slices { get; init; }
}
