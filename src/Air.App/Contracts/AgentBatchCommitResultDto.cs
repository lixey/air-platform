namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentBatchCommitResultDto
{
  public List<AgentCommitResultDto> Committed { get; set; } = [];
  public List<string> RefreshServices { get; set; } = [];
}
