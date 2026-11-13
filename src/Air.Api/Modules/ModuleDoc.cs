namespace Platform.Api.Modules;

public sealed record ModuleDoc(
  string Title,
  string Content,
  int Priority = 150
);
