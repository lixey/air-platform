namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentBatchCommitResultDto
{
  public List<AgentCommitResultDto> Committed { get; set; } = [];
  public List<string> RefreshServices { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? RunId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? BatchCode { get; set; }
}
