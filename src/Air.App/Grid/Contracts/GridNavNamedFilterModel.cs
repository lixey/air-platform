namespace Platform.App.Grid.Contracts;

[UiContract]
public sealed record GridNavNamedFilterModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsRemembered { get; set; }
}
