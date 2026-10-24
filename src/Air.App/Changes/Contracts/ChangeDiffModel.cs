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
  public string? Service { get; init; }
  public string? ParentService { get; init; }
  public long? ParentId { get; init; }
  public bool CanRestore { get; init; }
  public string? RestoreLabel { get; init; }
  public string? RestoreConfirm { get; init; }
  public string? RestoredLabel { get; init; }
  public string? EmptyLabel { get; init; }
  public IReadOnlyList<ChangeDiffFieldDto> Fields { get; init; } = [];
}
