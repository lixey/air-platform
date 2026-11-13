namespace Platform.Agent.Internals;

internal sealed record ChildRowParseScope(
  ProposalBuildContext Context,
  ProposalDraftState ParentDraft,
  IFormService ChildForm,
  Dictionary<string, object?> ChildDefaults,
  IReadOnlyList<FormElementDto> EditableElements);
