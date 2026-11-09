namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentSyncSourceDto
{
  public string Id { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Subtitle { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Icon { get; set; }

  public bool AcceptsFile { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? FileAccept { get; set; }
}
