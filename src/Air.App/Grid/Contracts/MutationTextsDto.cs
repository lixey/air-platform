namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class MutationTextsDto
{
  public string ConflictTitle { get; set; } = string.Empty;
  public string ReloadLabel { get; set; } = string.Empty;
  public string UnknownTitle { get; set; } = string.Empty;
  public string UnknownDescription { get; set; } = string.Empty;
  public string VerifyLabel { get; set; } = string.Empty;
  public string CommittedWarningTitle { get; set; } = string.Empty;
  public string SelectionCorrectionLabel { get; set; } = string.Empty;
}
