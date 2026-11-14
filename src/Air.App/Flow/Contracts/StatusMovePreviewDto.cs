namespace Platform.App.Flow.Contracts;

public sealed class StatusMovePreviewDto
{
  public required long RecordId { get; init; }
  public int? Ver { get; init; }
  public string? RecordCaption { get; init; }
  public required string StatusPath { get; init; }
  public required string TransitionName { get; init; }
  public required string TransitionLabel { get; init; }
  public required long CurrentStatusId { get; init; }
  public required string CurrentStatusName { get; init; }
  public required long TargetStatusId { get; init; }
  public required string TargetStatusName { get; init; }
  public string? TargetStatusColor { get; init; }
  public IReadOnlyList<FormElementDto> AskElements { get; init; } = [];
  public Dictionary<string, object?> AskValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);
  public List<MessageDto> Warnings { get; init; } = [];
}
