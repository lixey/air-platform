namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentProposalChildRowDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public long? Id { get; set; }

  public Dictionary<string, string> Cells { get; set; } = [];
}
