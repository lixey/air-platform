namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormInfoModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public bool? IsGrouped { get; set; }
}
