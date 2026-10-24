namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportDataDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasDateRange { get; init; }

  public List<ReportFilterGroupDto> FilterGroups { get; init; } = [];
  public Dictionary<string, string> ActiveFilters { get; init; } = new();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportKpiDto>? Kpis { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportChartDto>? Charts { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ReportTableDto? Table { get; init; }
}
