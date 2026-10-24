namespace Platform.App.Screens.Contracts;

/// <summary>
/// Root DTO returned by a screen route: its layout and optional initial data payload.
/// </summary>
[UiContract]
public sealed class ScreenDto
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public PanelLayoutDto? Layout { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public object? Data { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? OverlayWidth { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? OverlayHeight { get; set; }
}
