namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavToggleModel : NavButtonModel
{
  public string Path { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool TargetsRoot { get; set; }
}
