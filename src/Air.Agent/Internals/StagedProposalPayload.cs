namespace Platform.Agent.Internals;

internal sealed class StagedProposalPayload
{
  public string Operation { get; set; } = AgentProposalOperation.Create;
  public string Service { get; set; } = string.Empty;
  public string? Transition { get; set; }
  public long? RecordId { get; set; }
  public int? Ver { get; set; }
  public string? StepKey { get; set; }
  public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  public List<string> ExplicitFields { get; set; } = [];
  public List<string> DependsOnProposalIds { get; set; } = [];
  public List<PendingNewReference> NewReferences { get; set; } = [];
  public List<StagedChildGroupPayload> Children { get; set; } = [];
}
