namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class SelectPatchDto
{
  public string TargetField { get; set; } = string.Empty;
  public string SourceColumn { get; set; } = string.Empty;
}
