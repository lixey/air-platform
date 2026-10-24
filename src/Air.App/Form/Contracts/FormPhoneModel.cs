namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormPhoneModel : FormElementModel
{
  public List<string> Masks { get; set; } = [];
}
