namespace Platform.Agent.Internals;

internal readonly record struct AgentStepCommitOutcome(
  long RecordId,
  string RecordLabel,
  string FormServicePath,
  List<string> RefreshServices,
  List<MessageDto>? Warnings,
  string? NoticeTitle = null,
  List<MessageDto>? NoticeMessages = null);
