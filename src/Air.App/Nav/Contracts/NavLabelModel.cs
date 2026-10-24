namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavLabelModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Path { get; set; }
}
