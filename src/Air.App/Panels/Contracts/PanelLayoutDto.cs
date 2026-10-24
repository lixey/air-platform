namespace Platform.App.Panels.Contracts;

[UiContract]
public sealed class PanelLayoutDto
{
  public string Key { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Gap { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? HideSecondaryUnder { get; set; }

  public PanelNodeDto Root { get; set; } = new();
}
