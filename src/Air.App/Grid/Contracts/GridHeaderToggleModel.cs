namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridHeaderToggleModel : HeaderModel
{
  public List<string> Toggles { get; set; } = [];
}
