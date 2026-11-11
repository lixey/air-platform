namespace Platform.Agent.Internals;

internal sealed record OwnedProposal(
  AgentProposalDto Dto,
  AgentOwnerScope Owner,
  DateTimeOffset LastAccessUtc
);
