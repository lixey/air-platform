namespace Platform.Agent.Internals;

internal sealed record OwnedAttachment(
  AgentAttachmentDto Dto,
  AgentOwnerScope Owner,
  DateTimeOffset LastAccessUtc
);
