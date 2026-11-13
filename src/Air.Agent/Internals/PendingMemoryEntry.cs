namespace Platform.Agent.Internals;

internal sealed record PendingMemoryEntry(
  AgentOwnerScope Owner,
  string Kind,
  string Title,
  string Content,
  DateTimeOffset CreatedAtUtc
);
