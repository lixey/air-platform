namespace Platform.Agent.Contracts;

[UiContract]
public static class AgentProposalStatus
{
  public const string Pending = "pending";
  public const string Confirming = "confirming";
  public const string Committing = "committing";
  public const string Committed = "committed";
  public const string Superseded = "superseded";
}
