namespace Platform.App.Contracts;

[UiContract]
public static class AgentStreamEventType
{
  public const string Status = "status";
  public const string Delta = "delta";
  public const string Supersede = "supersede";
  public const string Refresh = "refresh";
  public const string Proposal = "proposal";
  public const string Error = "error";
  public const string Done = "done";
}
