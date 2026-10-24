namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormTimeModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinuteStep { get; set; }
}
