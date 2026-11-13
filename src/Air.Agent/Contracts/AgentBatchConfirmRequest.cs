namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentBatchConfirmRequest
{
  public List<string> ProposalIds { get; set; } = [];
}
