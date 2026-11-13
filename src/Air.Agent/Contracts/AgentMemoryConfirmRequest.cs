namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentMemoryConfirmRequest
{
  public string PendingToken { get; set; } = string.Empty;
}
