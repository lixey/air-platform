namespace Platform.App.Flow.Contracts;

public sealed class FlowDescriptionDto
{
  public string? StatusPath { get; init; }
  public IReadOnlyList<OptionDto> Statuses { get; init; } = [];
  public IReadOnlyList<FlowTransitionDescriptorDto> Transitions { get; init; } = [];
}
