namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavAddModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Service { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public OverlayDto? Overlay { get; set; }
}
