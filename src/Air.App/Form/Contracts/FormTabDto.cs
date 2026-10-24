namespace Platform.App.Form.Contracts;

[UiContract]
public sealed class FormTabDto
{
  public string Key { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }
}
