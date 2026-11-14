namespace Platform.Agent.Contracts;

[UiContract]
public sealed class ConfirmProposalRequest
{
  public string ProposalId { get; set; } = string.Empty;
}
