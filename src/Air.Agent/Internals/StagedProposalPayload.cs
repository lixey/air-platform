namespace Platform.Agent.Internals;

internal sealed class StagedProposalPayload
{
  public string Service { get; set; } = string.Empty;
  public long? RecordId { get; set; }
  public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  public List<PendingNewReference> NewReferences { get; set; } = [];
  public string? ChildFormService { get; set; }
  public bool HasLinesUpdate { get; set; }
  public string? LinesMode { get; set; }
  public List<Dictionary<string, object?>> Lines { get; set; } = [];
}
