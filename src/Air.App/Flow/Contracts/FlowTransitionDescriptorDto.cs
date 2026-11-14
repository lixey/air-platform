namespace Platform.App.Flow.Contracts;

public sealed class FlowTransitionDescriptorDto
{
  public required string Name { get; init; }
  public required string Label { get; init; }
  public string? Title { get; init; }
  public string? Confirm { get; init; }
  public required IReadOnlyList<long> From { get; init; }
  public required long To { get; init; }
  public string? ToStatusName { get; init; }
  public string? ToStatusColor { get; init; }
  public IReadOnlyList<FormElementDto> AskElements { get; init; } = [];
}
