namespace Platform.App.Grid.Contracts;

// Grid Nav Models
[UiContract]
public sealed record GridNavSearchModel : NavElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }
}
