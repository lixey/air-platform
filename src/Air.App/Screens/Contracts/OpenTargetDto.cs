namespace Platform.App.Screens.Contracts;

[UiContract]
public sealed class OpenTargetDto
{
  public string Service { get; init; } = string.Empty;

  public long Id { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayDto? Overlay { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<string>? RefreshServices { get; init; }
}
