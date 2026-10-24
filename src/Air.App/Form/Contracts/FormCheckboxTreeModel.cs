namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormCheckboxTreeModel : FormElementModel
{
  public List<CheckboxTreeNodeDto> Nodes { get; set; } = [];
}
