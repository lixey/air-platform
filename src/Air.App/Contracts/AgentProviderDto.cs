namespace Platform.App.Contracts;

[UiContract]
public sealed class AgentProviderDto
{
  public string Handle { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public bool IsAvailable { get; set; }
}
