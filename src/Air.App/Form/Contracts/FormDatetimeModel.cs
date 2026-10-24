namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormDatetimeModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinuteStep { get; set; }
}
