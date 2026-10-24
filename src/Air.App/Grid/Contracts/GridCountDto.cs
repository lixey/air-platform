namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed class GridCountDto
{
  public int Total { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string, int>? NamedFilterBadges { get; set; }
}
