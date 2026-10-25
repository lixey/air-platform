namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportTableDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Key { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Tab { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool Searchable { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool Sortable { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool Compact { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxHeight { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? EmptyText { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? FilterField { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<ReportFilterItemDto>? FilterChips { get; init; }

  public List<ReportColumnDto> Columns { get; init; } = [];
  public List<Dictionary<string, object?>> Rows { get; init; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<Dictionary<string, object?>>? BottomRows { get; init; }
}
