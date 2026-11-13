namespace Platform.Agent.Contracts;

public sealed record AgentDomainKnowledgeEntry(
  string? Module,
  string Title,
  string Content,
  int Priority = 150
);
