namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormMoneyModel : FormElementModel
{
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Currency { get; set; }
}
