namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormCheckboxSetModel : FormElementModel
{
  public List<OptionDto> Options { get; set; } = [];
}
