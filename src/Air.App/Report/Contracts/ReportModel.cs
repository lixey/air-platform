namespace Platform.App.Report.Contracts;

[UiContract]
public sealed record ReportModel : PanelModel
{
  public string Service { get; init; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; init; }
}
