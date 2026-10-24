namespace Platform.App.Overlay.Contracts;

[UiContract]
public sealed class OverlayScreenDto
{
  public string Service { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public object? Params { get; set; }
}
