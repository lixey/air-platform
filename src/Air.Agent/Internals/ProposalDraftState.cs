namespace Platform.Agent.Internals;

internal sealed class ProposalDraftState
{
  public required JsonElement Args { get; init; }
  public required JsonElement ValuesSource { get; init; }
  public required bool HasNestedValues { get; init; }
  public required bool IsEdit { get; init; }
  public required long? RecordId { get; init; }
  public required GridCreateDescriptor Target { get; init; }
  public string? MultiSelectMode { get; init; }
  public required Dictionary<string, object?> FormValues { get; init; }
  public required Dictionary<string, List<Dictionary<string, object?>>> Dictionaries { get; init; }
  public HashSet<string> ExplicitlyProvided { get; } = new(StringComparer.OrdinalIgnoreCase);
  public List<PendingNewReference> NewReferences { get; } = [];
  public List<(FormElementDto Element, AgentProposalDto TargetProposal)> LinkedSelectPatches { get; } = [];

  public bool TryReadInputProperty(string name, out JsonElement value) =>
    AgentValueHelpers.TryGetPropertyIgnoreCase(ValuesSource, name, out value) ||
    HasNestedValues && AgentValueHelpers.TryGetPropertyIgnoreCase(Args, name, out value);

  public bool TryReadEnvelopeProperty(string name, out JsonElement value) =>
    AgentValueHelpers.TryGetPropertyIgnoreCase(Args, name, out value) ||
    HasNestedValues && AgentValueHelpers.TryGetPropertyIgnoreCase(ValuesSource, name, out value);
}
