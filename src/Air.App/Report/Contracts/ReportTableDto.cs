namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportTableDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; init; }

  public List<ReportColumnDto> Columns { get; init; } = [];
  public List<Dictionary<string, object?>> Rows { get; init; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<Dictionary<string, object?>>? BottomRows { get; init; }
}
