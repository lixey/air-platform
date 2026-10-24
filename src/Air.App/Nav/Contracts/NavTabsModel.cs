namespace Platform.App.Nav.Contracts;

[UiContract]
public sealed record NavTabsModel : NavButtonModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool IsPagedOnly { get; set; }
}
