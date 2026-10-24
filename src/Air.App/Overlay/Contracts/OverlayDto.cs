namespace Platform.App.Overlay.Contracts;

[UiContract]
public sealed class OverlayDto
{
  public string Key { get; set; } = string.Empty;

  [OneOf(typeof(OverlayPresentation))]
  public string Presentation { get; set; } = OverlayPresentation.Dialog;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public PanelLayoutDto? Layout { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayScreenDto? Screen { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Width { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Height { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? MaxWidth { get; set; }

  [ClientOptional]
  public bool ClosesOnEscape { get; set; } = true;

  [ClientOptional]
  public bool ClosesOnOutside { get; set; } = true;
}
