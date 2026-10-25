namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportColumnDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;

  [OneOf(typeof(ReportColumnKind))]
  public string Kind { get; init; } = ReportColumnKind.Text;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Sticky { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Width { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Decimals { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? Sortable { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Align { get; init; }
}
