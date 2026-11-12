namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormDateTimeModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinuteStep { get; set; }
}
