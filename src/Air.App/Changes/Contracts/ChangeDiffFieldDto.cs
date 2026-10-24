namespace Platform.App.Changes.Contracts;

[UiContract]
public sealed record ChangeDiffFieldDto
{
  public string Key { get; init; } = string.Empty;
  public string Label { get; init; } = string.Empty;
  public string? Before { get; init; }
  public string? After { get; init; }
  public bool IsChanged { get; init; }
}
