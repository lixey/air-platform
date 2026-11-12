namespace Platform.Agent.Internals;

internal sealed record AgentFlowTransitionDescriptor(
  string Name,
  string Label,
  string? Title,
  IReadOnlyList<long> FromStatuses,
  IReadOnlyList<string> FromStatusNames,
  long ToStatus,
  string ToStatusName,
  string? ToStatusColor,
  string? RequiredRight,
  RecordAsk? Ask,
  IReadOnlyList<FormElementDto> AskElements,
  IReadOnlyList<RecordAvailability> Offers,
  Type FlowServiceType);
