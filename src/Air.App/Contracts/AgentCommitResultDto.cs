namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentCommitResultDto
{
  public string ProposalId { get; set; } = string.Empty;
  public long RecordId { get; set; }
  public string RecordLabel { get; set; } = string.Empty;
  public string FormService { get; set; } = string.Empty;
  public List<string> RefreshServices { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<AgentCommitResultDto>? AlsoCommitted { get; set; }
}
