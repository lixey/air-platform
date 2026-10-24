namespace Platform.App.Form.Contracts;

[UiContract]
public sealed record FormAskModel : PanelModel
{
  public List<FormElementDto> Elements { get; set; } = [];

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Title { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Message { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(UiColor))]
  public string? Color { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public bool HasDefaults { get; set; }
}
