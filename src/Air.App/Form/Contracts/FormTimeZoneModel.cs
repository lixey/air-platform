namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormTimeZoneModel : FormElementModel
{
  public List<string> Zones { get; set; } = [];
  public string Inherit { get; set; } = string.Empty;
}
