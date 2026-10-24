namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavTabModel : NavButtonModel
{
  public string Group { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }
}
