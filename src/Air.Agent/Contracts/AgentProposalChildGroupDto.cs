namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentProposalChildGroupDto
{
  public string ChildKey { get; set; } = string.Empty;
  public string ChildService { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;

  [OneOf(typeof(AgentProposalChildMode))]
  public string Mode { get; set; } = AgentProposalChildMode.Append;

  public List<AgentProposalChildColumnDto> Columns { get; set; } = [];
  public List<AgentProposalChildRowDto> Rows { get; set; } = [];
}
