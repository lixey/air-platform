namespace Platform.Agent.Contracts;

[UiContract]
public sealed class AgentSavedMemoryDto
{
  public long Id { get; set; }
  public string Category { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Content { get; set; } = string.Empty;

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  [OneOf(typeof(AgentSavedMemoryStatus))]
  public string? Status { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? PendingToken { get; set; }
}
