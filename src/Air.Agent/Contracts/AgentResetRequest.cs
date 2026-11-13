namespace Platform.Agent.Contracts;

[UiContract]
[ClientOptional]
public sealed class AgentResetRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SessionId { get; set; }
}
