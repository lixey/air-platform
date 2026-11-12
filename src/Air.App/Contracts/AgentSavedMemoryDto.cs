namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentSavedMemoryDto
{
  public long Id { get; set; }
  public string Category { get; set; } = string.Empty;
  public string Title { get; set; } = string.Empty;
  public string Content { get; set; } = string.Empty;
}
