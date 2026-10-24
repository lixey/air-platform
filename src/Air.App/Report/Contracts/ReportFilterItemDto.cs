namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportFilterItemDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; init; }
}
