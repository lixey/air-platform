namespace Platform.Agent.Contracts;

[UiContract]
public static class AgentProposalOperation
{
  public const string Create = "create";
  public const string Update = "update";
  public const string MoveStatus = "move-status";
  public const string RunAction = "run-action";
}
