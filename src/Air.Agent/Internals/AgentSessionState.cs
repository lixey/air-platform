namespace Platform.Agent.Internals;

internal sealed class AgentSessionState(AgentOwnerScope owner)
{
  public AgentOwnerScope Owner { get; } = owner;
  public DateTimeOffset LastAccessUtc { get; set; } = DateTimeOffset.UtcNow;
  public SemaphoreSlim TurnGate { get; } = new(1, 1);
  public ConcurrentDictionary<string, string> ConversationIds { get; } = new(StringComparer.OrdinalIgnoreCase);
  public List<AgentProposalDto> Proposals { get; } = [];
  public ConcurrentQueue<AgentProposalDto> PendingProposals { get; } = new();
  public ConcurrentQueue<string> SupersededProposalIds { get; } = new();
  public Dictionary<string, int> TurnStepCounters { get; } = new(StringComparer.OrdinalIgnoreCase);
}
