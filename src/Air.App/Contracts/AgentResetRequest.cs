namespace Platform.App.Contracts;

[UiContract]
[ClientOptional]
public sealed class AgentResetRequest
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? SessionId { get; set; }
}
