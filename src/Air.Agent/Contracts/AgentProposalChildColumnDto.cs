namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentProposalChildColumnDto
{
  public string Key { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;
  public bool IsNumeric { get; set; }
}
