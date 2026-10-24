namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavSearchModel : NavElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; set; }

  public string Value { get; set; } = string.Empty;
}
