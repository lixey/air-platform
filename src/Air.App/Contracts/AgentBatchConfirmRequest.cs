namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentBatchConfirmRequest
{
  public List<string> ProposalIds { get; set; } = [];
}
