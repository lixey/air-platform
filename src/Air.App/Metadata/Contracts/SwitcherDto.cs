namespace Platform.App.Metadata.Contracts;

[UiContract]
public record SwitcherDto
{
  public string Label { get; set; } = string.Empty;
  public Reference Current { get; set; } = new();

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public List<Reference>? Options { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Service { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Method { get; set; }
}
