namespace Platform.App.Report.Contracts;

[UiContract]
public sealed class ReportRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsDataOnly { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, string>? Filters { get; set; }
}
