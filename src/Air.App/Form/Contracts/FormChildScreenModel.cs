namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormChildScreenModel : PanelModel
{
  public string Service { get; set; } = string.Empty;
  public string ParentService { get; set; } = string.Empty;
  public long ParentId { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public NavModel? Tabs { get; set; }
}
