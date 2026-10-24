namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentStatusDto
{
  public string ActiveProvider { get; set; } = string.Empty;
  public string DefaultModel { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? PhotoModel { get; set; }

  public List<AgentProviderDto> Providers { get; set; } = [];
  public List<AgentModelDto> Models { get; set; } = [];
}
