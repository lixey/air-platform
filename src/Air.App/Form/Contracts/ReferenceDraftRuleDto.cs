namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record ReferenceDraftRuleDto
{
  public string SourceField { get; set; } = string.Empty;
  public string DraftField { get; set; } = string.Empty;
}
