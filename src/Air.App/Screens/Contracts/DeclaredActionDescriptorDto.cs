namespace Platform.App.Screens.Contracts;

public sealed class DeclaredActionDescriptorDto
{
  public required string Service { get; init; }
  public required string Handle { get; init; }
  public required string Method { get; init; }
  public required string Label { get; init; }
  public string? Title { get; init; }
  public string? Confirm { get; init; }
  public string? Color { get; init; }
  public required string SelectionMode { get; init; }
  public bool IsAutomationAllowed { get; init; }
  public IReadOnlyList<FormElementDto> AskElements { get; init; } = [];
}
