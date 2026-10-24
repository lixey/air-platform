namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavNamedFilterGroupModel : NavButtonModel
{
  public List<GridNavNamedFilterModel> Items { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsRemembered { get; set; }
}
