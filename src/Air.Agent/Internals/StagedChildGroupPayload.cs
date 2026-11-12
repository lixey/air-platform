namespace Platform.Agent.Internals;

internal sealed class StagedChildGroupPayload
{
  public string ChildKey { get; set; } = string.Empty;
  public string ChildGridService { get; set; } = string.Empty;
  public string ChildFormService { get; set; } = string.Empty;
  public string Mode { get; set; } = AgentProposalChildMode.Append;
  public bool HasUpdate { get; set; }
  public List<Dictionary<string, object?>> Rows { get; set; } = [];
}
