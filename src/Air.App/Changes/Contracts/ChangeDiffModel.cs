namespace Platform.App.Changes.Contracts;

[UiContract]
public sealed record ChangeDiffModel : PanelModel
{
  public long Id { get; init; }
  public string Title { get; init; } = string.Empty;
  public string? Subtitle { get; init; }
  public string Operation { get; init; } = string.Empty;
  public string OperationKey { get; init; } = string.Empty;
  public string OperationColor { get; init; } = UiColor.Slate;
  public string? EmptyLabel { get; init; }
  public string? SourceBadge { get; init; }
  public string? SourceBadgeColor { get; init; }
  public string? RelatedTitle { get; init; }
  public IReadOnlyList<ChangeDiffFieldDto> Fields { get; init; } = [];
  public IReadOnlyList<ChangeDiffRelatedItemDto> RelatedChanges { get; init; } = [];
}
