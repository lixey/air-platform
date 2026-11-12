namespace Platform.Agent.Internals;

internal sealed record AgentEditableChildDescriptor(
  ChildGridBuilder Builder,
  string ChildKey,
  string ChildGridService,
  string ChildFormService,
  Type ChildGridType,
  Type ChildFormType,
  string Title);
