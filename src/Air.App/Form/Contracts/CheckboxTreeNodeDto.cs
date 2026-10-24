namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class CheckboxTreeNodeDto
{
  public string Key { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Note { get; set; }

  public bool IsDisabled { get; set; }

  public List<CheckboxTreeNodeDto> Children { get; set; } = [];
}
